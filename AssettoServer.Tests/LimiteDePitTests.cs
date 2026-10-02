using NUnit.Framework;
using TrackLimitsPlugin;

namespace AssettoServer.Tests;

/// <summary>
/// The pit limit comes out of the welcome message's compressed blob.
///
/// <para>
/// WHY THIS IS TESTED AND NOT EYEBALLED: the number reaches the driver's screen
/// and they drive by it. Reading it wrong is worse than not reading it -- a
/// confident wrong number earns a penalty the driver was actively trying to
/// avoid. And the failure is silent: a blob that decodes to nothing looks
/// exactly like a league that did not set a limit.
/// </para>
/// </summary>
public class LimiteDePitTests
{
    /// <summary>
    /// O blob REAL do servidor da liga, copiado do cfg/welcome.txt em 02/10/2026.
    /// Sintetizar um aqui testaria o meu proprio compressor, nao o do gerador.
    /// </summary>
    private const string WelcomeDaLiga =
        "Teste — Liga Veteranos\t\t\t\t$CSP0:eJxljUEOwjAMBO+R8oc+oQiJGwejmNYiTYLtUKoK+f+/IEAPlTjuanZ2LaRiUhCDRZpIkV/e/fJtGrtzd+q3TGmwggmiLq0OTA80HTnXYfwnLEKRhh29827FpzIY14jS7Iz3SoyWcP5gds1sX91m2730u3kAhbbWhVHsEvOcTBQUG3bw7g2OejXv";

    [Test]
    public void LeOLimiteDoBlobReal()
    {
        Assert.That(LimiteDePit.Ler(WelcomeDaLiga), Is.EqualTo(60));
    }

    // NULO, E NAO UM PADRAO. Ha um padrao tentador -- a liga usa 50 -- e adota-lo
    // aqui faria o HUD mostrar um numero confiante que ninguem conferiu.
    [TestCase(null, TestName = "welcome nulo")]
    [TestCase("", TestName = "welcome vazio")]
    [TestCase("Bem-vindo a liga!", TestName = "welcome sem blob nenhum")]
    [TestCase("Oi $CSP0:naoEhBase64Valido!!!", TestName = "blob que nao descomprime")]
    public void SemLimiteDevolveNulo(string? welcome)
    {
        Assert.That(LimiteDePit.Ler(welcome), Is.Null);
    }

    /// <summary>
    /// Descomprime, mas nao define limite -- um servidor pode mandar opcoes do
    /// CSP sem chave de pit, e isso nao e erro nenhum.
    ///
    /// <para>
    /// O BLOB AQUI FOI GERADO DE VERDADE: zlib de uma secao
    /// <c>EXTRA_DATA</c> sem chave de pit. A primeira versao deste teste
    /// usava base64 inventado a mao que NAO descomprimia, entao ele exercitava o
    /// caminho da excecao e nao o da chave ausente -- os dois devolvem nulo, e o
    /// teste passava provando outra coisa.
    /// </para>
    /// </summary>
    [Test]
    public void BlobQueDescomprimeMasNaoTemAChaveDevolveNulo()
    {
        const string semLimite = "x$CSP0:eJyLdo0ICXKMd3EMcYzlCokMcg2Od/LxD/eLDw5xDHFVsFUw5AIAvHIJ0A==";
        Assert.That(LimiteDePit.Ler(semLimite), Is.Null);
    }

    /// <summary>
    /// A variante url-safe do base64, com <c>-</c> e <c>_</c> e sem padding.
    ///
    /// <para>
    /// SEM ESTE TESTE A NORMALIZACAO ERA CODIGO MORTO: removi o
    /// <c>Replace</c> e a suite inteira continuou verde, porque todo blob que eu
    /// tinha usava o alfabeto padrao. O blob viaja num arquivo de texto e nao ha
    /// garantia de qual alfabeto quem o gerou escolheu.
    /// </para>
    /// </summary>
    [Test]
    public void LeOBlobNaVarianteUrlSafe()
    {
        const string urlSafe = "$CSP0:eJxljUEOwjAMBO-R8oc-oQiJGwejmNYiTYLtUKoK-f-_IEAPlTjuanZ2LaRiUhCDRZpIkV_e_fJtGrtzd-q3TGmwggmiLq0OTA80HTnXYfwnLEKRhh29827FpzIY14jS7Iz3SoyWcP5gds1sX91m2730u3kAhbbWhVHsEvOcTBQUG3bw7g2OejXv";
        Assert.That(LimiteDePit.Ler(urlSafe), Is.EqualTo(60));
    }

    /// <summary>
    /// Base64 sem o padding final, que e como a variante url-safe costuma viajar.
    ///
    /// <para>
    /// SEM ESTE TESTE O <c>PadRight</c> ERA CODIGO MORTO: apaguei a linha inteira
    /// e a suite continuou verde, porque o blob da liga tem comprimento multiplo
    /// de quatro e nunca precisou de padding. Este foi gerado de proposito para
    /// precisar -- <c>Convert.FromBase64String</c> lanca sem ele.
    /// </para>
    /// </summary>
    [Test]
    public void LeOBlobSemPadding()
    {
        const string semPadding = "$CSP0:eJyLDvAMCY4PDnB1dYn38fT1DHENiuWCcL19PRRsFUwNuLgA0aQJ7g";
        Assert.That(LimiteDePit.Ler(semPadding), Is.EqualTo(50));
    }
}
