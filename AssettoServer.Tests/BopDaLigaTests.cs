using AssettoServer.Server;
using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Tests;

/// <summary>
/// O BOP por CARRO, que é o que o <c>entry_list</c> não consegue fazer.
///
/// <para>
/// Lastro e restritor no <c>entry_list</c> são da VAGA, e a vaga só tem dono
/// quando a liga sabe quem vai correr. Num 4fun de entrada livre não há piloto
/// para mapear — e era ali que o grid ficava desequilibrado. Este BOP é
/// aplicado pelo servidor quando a vaga é tomada, olhando o MODELO.
/// </para>
///
/// <para>
/// O QUE ESTE TESTE PROTEGE: os limites que o próprio validador do AssettoServer
/// impõe (lastro ≥ 0, restritor 0–400) e, principalmente, o ZERO de quem não
/// tem BOP. A vaga é reusada por quem entrar depois e <c>EntryCar.Reset()</c>
/// não mexe em lastro: sem zerar, o lastro de uma etapa ficaria grudado na
/// vaga até o servidor reiniciar, e o próximo a sentar ali correria com o peso
/// de outro sem nada na tela explicando.
/// </para>
/// </summary>
public class BopDaLigaTests
{
    private static BopDaLiga Montar(params (string carro, float kg, int restritor)[] linhas)
        => new(linhas.Select(l => new BopDeCarroConfiguration
        {
            Carro = l.carro,
            LastroKg = l.kg,
            Restritor = l.restritor,
        }).ToList());

    [Test]
    public void SEM_TABELA_NAO_MEXE_EM_NADA()
    {
        // Servidor sem BOP cadastrado tem de deixar o `entry_list` valendo: e
        // assim que uma etapa com lastro por vaga continua funcionando.
        var bop = Montar();
        Assert.That(bop.Existe, Is.False);
        Assert.That(bop.Para("sr_clio_cup"), Is.Null);
    }

    [Test]
    public void LASTRO_NEGATIVO_VIRA_ZERO()
    {
        // No ACC o lastro negativo alivia um carro fraco. No AC1 o validador
        // do servidor exige >= 0, e um negativo derrubaria o servidor na
        // partida -- a etapa nao sobe, que e pior que correr sem BOP.
        var bop = Montar(("sr_clio_cup", -20f, 0));
        Assert.That(bop.Para("sr_clio_cup")!.Value.LastroKg, Is.EqualTo(0f));
    }

    [Test]
    public void RESTRITOR_FORA_DA_FAIXA_E_APARADO()
    {
        // 0 a 400 e a faixa que o ACServerConfigurationValidator aceita.
        var bop = Montar(("x", 0f, 900));
        Assert.That(bop.Para("x")!.Value.Restritor, Is.EqualTo(400));
    }

    [Test]
    public void CARRO_VAZIO_NAO_ENTRA_NA_TABELA()
    {
        // Linha em branco no cadastro nao pode virar uma chave vazia que
        // casaria com carro nenhum -- ou, pior, com um modelo vazio.
        var bop = Montar(("   ", 50f, 10));
        Assert.That(bop.Existe, Is.False);
        Assert.That(bop.Para(""), Is.Null);
    }

    [Test]
    public void A_PASTA_DO_CARRO_NAO_DIFERENCIA_MAIUSCULA()
    {
        // O cadastro da liga tem `TRR_GT3_mercedes-amg_gt3_evo` com maiuscula
        // e o `entry_list` sai como o admin escreveu. Casar so exato faria o
        // BOP sumir por causa de uma letra.
        var bop = Montar(("TRR_GT3_Mercedes-AMG_GT3_EVO", 30f, 5));
        var achado = bop.Para("trr_gt3_mercedes-amg_gt3_evo");
        Assert.That(achado, Is.Not.Null);
        Assert.That(achado!.Value.LastroKg, Is.EqualTo(30f));
        Assert.That(achado.Value.Restritor, Is.EqualTo(5));
    }
}
