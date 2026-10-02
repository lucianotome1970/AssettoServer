using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace TrackLimitsPlugin;

/// <summary>
/// Reads the pit speed limit out of the server's own welcome message.
///
/// <para>
/// ONE SOURCE, AND ELE E O MESMO QUE O CLIENTE LE. The limit does not exist as
/// a server_cfg key -- the acServer has none, and 80 km/h is hardcoded in
/// acs.exe. The only channel is a compressed blob appended to the welcome
/// message, which CSP clients decode and everyone else sees as plain text.
/// </para>
///
/// <para>
/// WHY NOT A CONFIG KEY OF OUR OWN: the league already sets the limit once, in
/// the pack the Pit Engineer generates. A second key here would be a copy, and
/// a copy drifts -- the day they disagree, the HUD shows one number and the game
/// enforces another. A driver trusting a wrong number on screen is exactly the
/// failure this plugin exists to prevent.
/// </para>
///
/// <para>
/// NULL WHEN IT CANNOT BE READ, and the caller says nothing rather than guessing.
/// There is a tempting default -- the league uses 50 -- and taking it would mean
/// showing a confident number that nobody verified.
/// </para>
/// </summary>
public static class LimiteDePit
{
    /// <summary>The marker CSP looks for in the welcome message.</summary>
    private static readonly Regex Blob = new(@"\$CSP0:([A-Za-z0-9+/=_-]+)", RegexOptions.Compiled);

    private static readonly Regex Chave =
        new(@"SPEED_KMH\s*=\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static int? Ler(string? welcomeMessage)
    {
        if (string.IsNullOrEmpty(welcomeMessage)) return null;

        var m = Blob.Match(welcomeMessage);
        if (!m.Success) return null;

        var texto = Descomprimir(m.Groups[1].Value);
        if (texto == null) return null;

        var k = Chave.Match(texto);
        return k.Success && int.TryParse(k.Groups[1].Value, out var kmh) ? kmh : null;
    }

    /// <summary>
    /// base64 + zlib, que e o formato do blob.
    ///
    /// <para>
    /// ACEITA A VARIANTE URL-SAFE (<c>-</c> e <c>_</c>) e preenche o padding: o
    /// blob viaja num arquivo de texto e nao ha garantia de qual alfabeto quem o
    /// gerou usou.
    /// </para>
    /// </summary>
    private static string? Descomprimir(string base64)
    {
        try
        {
            var normalizado = base64.Replace('-', '+').Replace('_', '/');
            normalizado = normalizado.PadRight(
                normalizado.Length + (4 - normalizado.Length % 4) % 4, '=');

            using var entrada = new MemoryStream(Convert.FromBase64String(normalizado));
            using var zlib = new ZLibStream(entrada, CompressionMode.Decompress);
            using var leitor = new StreamReader(zlib, Encoding.UTF8);
            return leitor.ReadToEnd();
        }
        catch (Exception)
        {
            // ENGOLE DE PROPOSITO. Um blob malformado nao pode derrubar o
            // servidor na partida -- o pior resultado aceitavel e o HUD mostrar
            // o aviso sem o numero.
            return null;
        }
    }
}
