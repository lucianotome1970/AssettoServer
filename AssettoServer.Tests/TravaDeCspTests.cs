using AssettoServer.Server.Configuration;
using NUnit.Framework;

namespace AssettoServer.Tests;

/// <summary>
/// Where the minimum CSP version rides: in the track name, or in the handshake.
///
/// <para>
/// WHY THIS IS TESTED: the track name is what every client checks BEFORE it
/// connects, so getting it wrong does not produce a bug report -- it produces a
/// driver who cannot join and is told, wrongly, that content is missing. That
/// is the failure we already met twice (CSP 0.3.0 preview649 on 07/10/2026 and
/// preview716 on 21/10/2026), and it costs an evening each time.
/// </para>
///
/// <para>
/// AND BECAUSE THE TWO JOBS LOOK ALIKE: the prefix also carries the physics
/// flags. A change that drops the version must not quietly drop those for
/// whoever uses them.
/// </para>
/// </summary>
public class TravaDeCspTests
{
    /// <summary>
    /// SEM A BANDEIRA, NADA MUDA. O prefixo e o que o AssettoServer sempre fez,
    /// e um servidor da liga que nao liga nada continua barrando cliente velho
    /// pelo nome da pista.
    /// </summary>
    [Test]
    public void TravaNoNomeDaPistaContinuaSendoOPadrao()
    {
        var opcoes = new CSPTrackOptions { Track = "sr_guapore", MinimumCSPVersion = 2651 };
        Assert.That(opcoes.ToString(), Is.EqualTo("csp/2651/../sr_guapore"));
    }

    /// <summary>
    /// COM A COBRANCA NO HANDSHAKE, O NOME SAI LIMPO -- e e so isso que muda
    /// aqui. A versao continua guardada, porque meia duzia de coisas dependem
    /// dela: o conserto de checksum de superficie, o `MinVersion` do handshake
    /// do CSP e a reserva da spline da IA.
    /// </summary>
    [Test]
    public void CobrandoNoHandshakeONomeDaPistaFicaLimpo()
    {
        var opcoes = new CSPTrackOptions
        {
            Track = "sr_guapore",
            MinimumCSPVersion = 2651,
            CheckOnHandshake = true
        };
        Assert.That(opcoes.ToString(), Is.EqualTo("sr_guapore"));
        Assert.That(opcoes.MinimumCSPVersion, Is.EqualTo(2651));
    }

    /// <summary>
    /// AS BANDEIRAS DE FISICA VIAJAM NO MESMO PREFIXO, entao quem as usa nao
    /// pode cobrar no handshake -- some a trava E somem elas.
    /// </summary>
    [Test]
    public void AsBandeirasDeFisicaSomemJuntoComOPrefixo()
    {
        var comPrefixo = new CSPTrackOptions
        {
            Track = "sr_guapore",
            MinimumCSPVersion = 2651,
            Flags = TrackOptionsFlags.CustomCarPhysics
        };
        Assert.That(comPrefixo.ToString(), Is.EqualTo("csp/2651/../B/../sr_guapore"));

        var noHandshake = new CSPTrackOptions
        {
            Track = "sr_guapore",
            MinimumCSPVersion = 2651,
            Flags = TrackOptionsFlags.CustomCarPhysics,
            CheckOnHandshake = true
        };
        Assert.That(noHandshake.ToString(), Is.EqualTo("sr_guapore"));
    }

    /// <summary>
    /// A LEITURA NAO MUDOU: um TRACK que ja venha com o prefixo escrito a mao
    /// continua sendo entendido, e e dai que sai a pista de verdade.
    /// </summary>
    [Test]
    public void LerUmNomeComPrefixoContinuaFuncionando()
    {
        var lido = CSPTrackOptions.Parse("csp/3465/../B/../sr_guapore");
        Assert.That(lido.Track, Is.EqualTo("sr_guapore"));
        Assert.That(lido.MinimumCSPVersion, Is.EqualTo(3465));
        Assert.That(lido.Flags, Is.EqualTo(TrackOptionsFlags.CustomCarPhysics));
        Assert.That(lido.CheckOnHandshake, Is.False);
    }

    /// <summary>
    /// O CONSERTO DE CHECKSUM SEGUE O PREFIXO, e nao o numero.
    /// </summary>
    /// <remarks>
    /// O servidor imita o cliente: em modo de pista estendida o CSP renomeia o
    /// primeiro SURFACE_0 antes de calcular o checksum do surfaces.ini, e o
    /// servidor faz igual para os dois baterem. Sem prefixo o cliente nao esta
    /// nesse modo -- imita-lo ali faz o servidor recusar um arquivo byte a byte
    /// identico ao dele, que foi o que aconteceu em 21/10/2026.
    /// </remarks>
    [Test]
    public void OConsertoDeChecksumSegueOPrefixoENaoONumero()
    {
        var comPrefixo = new CSPTrackOptions { Track = "sr_guapore", MinimumCSPVersion = 2651 };
        Assert.That(comPrefixo.VersionInTrackName, Is.True);

        var noHandshake = new CSPTrackOptions
        {
            Track = "sr_guapore",
            MinimumCSPVersion = 2651,
            CheckOnHandshake = true
        };
        Assert.That(noHandshake.VersionInTrackName, Is.False);

        var semVersao = new CSPTrackOptions { Track = "sr_guapore" };
        Assert.That(semVersao.VersionInTrackName, Is.False);
    }

    /// <summary>
    /// SEM VERSAO NENHUMA o nome ja saia limpo, e a bandeira nao inventa nada.
    /// </summary>
    [Test]
    public void SemVersaoMinimaONomeEAPistaPura()
    {
        Assert.That(new CSPTrackOptions { Track = "sr_guapore" }.ToString(),
            Is.EqualTo("sr_guapore"));
        Assert.That(new CSPTrackOptions { Track = "sr_guapore", CheckOnHandshake = true }.ToString(),
            Is.EqualTo("sr_guapore"));
    }
}
