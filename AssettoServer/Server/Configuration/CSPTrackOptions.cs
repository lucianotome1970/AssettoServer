using System;
using System.Text.RegularExpressions;

namespace AssettoServer.Server.Configuration;

public partial class CSPTrackOptions
{
    public uint? MinimumCSPVersion { get; init; }
    public required string Track { get; init; }
    public TrackOptionsFlags Flags { get; init; }

    /// <summary>
    /// Com isto, a versao minima NAO viaja no nome da pista -- quem cobra e o
    /// handshake, em <c>ACTcpClient</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O PREFIXO FAZ DOIS TRABALHOS, e eles sao separaveis. Ele cobra a versao,
    /// porque no AC puro nao ha outro jeito de exigir CSP antes de conectar, e
    /// carrega as bandeiras de fisica. O primeiro trabalho tem um preco: o
    /// cliente precisa SABER desmontar "csp/2651/../pista", e quem nao sabe nao
    /// acha a pista e nem tenta conectar.
    /// </para>
    /// <para>
    /// MEDIDO EM 21/10/2026: o CSP 0.3.0-preview716 nao desmonta, e recusa com
    /// "track csp/2651/../sr_guapore is missing" -- a string CRUA na mensagem e
    /// a assinatura disso. O preview649 fez o mesmo em 07/10. Sao versoes
    /// semanticamente MAIORES que o 0.2.0 exigido, barradas por nao entenderem
    /// a forma do nome.
    /// </para>
    /// <para>
    /// O SERVIDOR JA SABE A VERSAO DE QUEM CONECTA: ela vem na lista de
    /// recursos do CSP, no handshake. Cobrar la atende as duas linhas de
    /// cliente de uma vez, e a recusa passa a ter texto nosso em vez de um erro
    /// de pista faltando, que manda o piloto procurar conteudo que ele ja tem.
    /// </para>
    /// <para>
    /// O QUE SE PERDE: o Content Manager le o prefixo ANTES de conectar e avisa
    /// com botao de instalar. Sem ele, o piloto so descobre ao entrar. Mais
    /// claro no texto, mais tarde no tempo.
    /// </para>
    /// <para>
    /// AS BANDEIRAS VAO JUNTO, porque viajam no mesmo nome. Quem usa
    /// <see cref="TrackOptionsFlags"/> nao pode ligar isto.
    /// </para>
    /// </remarks>
    public bool CheckOnHandshake { get; init; }

    /// <summary>
    /// O nome da pista vai SAIR com o prefixo <c>csp/versao/..</c>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// QUEM PRECISA SABER DISTO NAO E SO O NOME. O cliente em modo de pista
    /// estendida muda o jeito de conferir o <c>surfaces.ini</c> -- renomeia o
    /// primeiro <c>SURFACE_0</c> antes de calcular --, e o servidor imita isso
    /// para os dois baterem (<c>ChecksumManager</c>). Imitar um cliente que NAO
    /// esta nesse modo derruba a entrada com "file doesn't match the server",
    /// sobre um arquivo byte a byte identico dos dois lados.
    /// </para>
    /// <para>
    /// MEDIDO EM 21/10/2026: foi exatamente o que aconteceu ao soltar o prefixo
    /// deixando o conserto amarrado em <c>MinimumCSPVersion.HasValue</c>.
    /// </para>
    /// </remarks>
    public bool VersionInTrackName => MinimumCSPVersion.HasValue && !CheckOnHandshake;
    
    [GeneratedRegex(@"^csp\/(\d+)\/\.\.(?:\/(\w+)\/\.\.)?\/(.+)$")]
    private static partial Regex TrackOptionsRegex();

    private const int FlagsOffset = 65; // 'A'

    public static CSPTrackOptions Parse(string track)
    {
        var match = TrackOptionsRegex().Match(track);
        if (match.Success)
        {
            var flags = TrackOptionsFlags.None;
            if (match.Groups[2].Success)
            {
                flags = (TrackOptionsFlags)match.Groups[2].Value[0] - FlagsOffset;
            }
            
            return new CSPTrackOptions
            {
                MinimumCSPVersion = uint.Parse(match.Groups[1].Value),
                Track = match.Groups[3].Value,
                Flags = flags
            };
        }

        return new CSPTrackOptions
        {
            Track = track
        };
    }

    public override string ToString()
    {
        if (!VersionInTrackName)
        {
            return Track;
        }

        var flags = Flags != TrackOptionsFlags.None
            ? $"/{(char)(Flags + FlagsOffset)}/.."
            : "";

        return $"csp/{MinimumCSPVersion.Value}/..{flags}/{Track}";
    }
}

[Flags]
public enum TrackOptionsFlags
{
    None = 0,
    CustomCarPhysics = 1,
    CustomTrackPhysics = 2,
    HidePitCrew = 4
}
