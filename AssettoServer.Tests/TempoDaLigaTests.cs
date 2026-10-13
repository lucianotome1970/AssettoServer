using AssettoServer.Network.Http;

namespace AssettoServer.Tests;

/// <summary>
/// O "sem volta" do servidor, na saída da rota <c>/api/liga/aovivo</c>.
///
/// <para>
/// O SERVIDOR MARCA "NÃO FECHOU VOLTA" COM <c>999999999</c>, e não com zero —
/// é o valor inicial de <c>EntryCarResult.BestLap</c> e <c>LastLap</c>, pensado
/// para que a primeira volta de qualquer um seja menor que ele.
/// </para>
///
/// <para>
/// REPASSADO CRU, ELE É UM TEMPO PLAUSÍVEL: atravessa a rede como número
/// válido e só vira absurdo na hora de formatar, a três camadas de distância —
/// o app escreveria "277:46:39" na coluna de melhor volta de quem acabou de
/// entrar no box. O combinado com o app é zero, que lá vira travessão.
/// </para>
/// </summary>
public class TempoDaLigaTests
{
    [Test]
    public void O_SEM_VOLTA_DO_SERVIDOR_VIRA_ZERO()
    {
        Assert.That(LigaController.Tempo(999999999), Is.EqualTo(0u));
    }

    [Test]
    public void RESULTADO_AUSENTE_TAMBEM_E_ZERO()
    {
        // O carro conecta antes de a sessão montar a tabela de resultados.
        Assert.That(LigaController.Tempo(null), Is.EqualTo(0u));
        Assert.That(LigaController.Tempo(0), Is.EqualTo(0u));
    }

    [Test]
    public void UMA_VOLTA_DE_VERDADE_PASSA_INTEIRA()
    {
        // 1:41.750 do print da classificação, em milissegundos.
        Assert.That(LigaController.Tempo(101750), Is.EqualTo(101750u));
        // E um tempo grande, mas possível, não pode ser confundido com o
        // sentinela: 20 minutos é volta de Nordschleife com problema.
        Assert.That(LigaController.Tempo(1200000), Is.EqualTo(1200000u));
    }
}
