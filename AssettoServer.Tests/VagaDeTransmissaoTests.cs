using AssettoServer.Server.Configuration.Kunos;

namespace AssettoServer.Tests;

/// <summary>
/// A chave SPECTATOR_MODE da lista de inscritos.
///
/// <para>
/// O UPSTREAM LIA E DESCARTAVA. <c>EntryCar.SpectatorMode</c> recebia o valor e
/// ninguem mais olhava; <c>IsSpectator</c>, o bit que o <c>CarListResponse</c>
/// manda para todos os clientes, nunca era ligado. Ligar so o bit, e mais nada,
/// MATA todo cliente que conecta -- medido em 03/10/2026, quatro crashes
/// seguidos, em qualquer vaga, com <c>UDPPAcket out of bounds</c>. O jogo honra
/// o bit e tira o carro da lista dele, ficando com 34 enquanto o servidor
/// seguia mandando 35.
/// </para>
///
/// <para>
/// Por isso o bit so e ligado junto com o filtro em
/// <see cref="AssettoServer.Server.VagaDeTransmissao"/>, que mantem as listas do
/// servidor do mesmo tamanho. Este teste cobre o elo de entrada -- o INI chegar
/// ate a inscricao, com o nome exato da chave; o outro arquivo cobre o filtro.
/// </para>
/// </summary>
public class VagaDeTransmissaoTests
{
    private static EntryList Ler(string conteudo)
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"entry_list_{Guid.NewGuid():N}.ini");
        File.WriteAllText(caminho, conteudo);
        try { return EntryList.FromFile(caminho); }
        finally { File.Delete(caminho); }
    }

    [Test]
    public void ADeclaracaoDeEspectadorChegaNaInscricao()
    {
        var lista = Ler("""
            [CAR_0]
            MODEL=sr_clio_cup
            SPECTATOR_MODE=0

            [CAR_1]
            MODEL=sr_clio_cup
            SPECTATOR_MODE=1
            DRIVERNAME=Transmissao
            """);

        Assert.That(lista.Cars, Has.Count.EqualTo(2));
        Assert.That(lista.Cars[0].SpectatorMode, Is.EqualTo(0), "piloto comum");
        Assert.That(lista.Cars[1].SpectatorMode, Is.EqualTo(1), "vaga de transmissao");
    }

    // O PADRAO TEM QUE SER ZERO. As listas que o Pit Engineer ja gerou escrevem
    // SPECTATOR_MODE=0 em todas as vagas, mas uma lista feita a mao pode omitir
    // a chave -- e se a ausencia virasse espectador, um grid inteiro deixaria de
    // ser de pilotos.
    [Test]
    public void SemAChaveNinguemEEspectador()
    {
        var lista = Ler("[CAR_0]\nMODEL=sr_clio_cup\n");
        Assert.That(lista.Cars[0].SpectatorMode, Is.EqualTo(0));
    }

    // GUARDA DO NOME DA CHAVE. E o nome exato que o acServer usa, e so ele; sem
    // isto, um erro de digitacao no entry_list.ini -- ou um rename no
    // IniField -- passaria como "nao e espectador", que e o silencio que
    // escondeu este recurso desde o inicio.
    [TestCase("SPECTATORMODE")]
    [TestCase("SPECTATOR")]
    [TestCase("Spectator_Mode_1")]
    public void ChaveParecidaNaoVale(string chave)
    {
        var lista = Ler($"[CAR_0]\nMODEL=sr_clio_cup\n{chave}=1\n");
        Assert.That(lista.Cars[0].SpectatorMode, Is.EqualTo(0));
    }
}
