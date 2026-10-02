using NUnit.Framework;
using TrackLimitsPlugin;

namespace AssettoServer.Tests;

/// <summary>
/// Quando repetir o limite de pit para um piloto.
///
/// <para>
/// ESTA CONTA JA FALHOU EM SILENCIO. O "ainda nao mandei" era
/// <c>long.MinValue</c> e a condicao era <c>agora - ultimo &gt; intervalo</c>:
/// a subtracao ESTOURA em 64 bits, da negativo, e o pacote nunca sai. Do lado
/// do piloto o sintoma e o mesmo de um canal de rede quebrado -- nada na tela
/// --, e custou quatro versoes do HUD procurando defeito onde nao havia.
/// </para>
/// </summary>
public class ReenvioDoLimiteTests
{
    [Test]
    public void AindaNaoMandouEntaoManda()
    {
        Assert.That(TrackLimitsService.DeveReenviar(null, 0, 10_000), Is.True);
    }

    // O CASO QUE ESTOUROU. Com o sentinela antigo esta era a unica combinacao
    // que importava, e era justamente a que dava falso.
    [TestCase(0L, TestName = "relogio zerado")]
    [TestCase(1L, TestName = "primeiro tique")]
    [TestCase(long.MaxValue, TestName = "relogio no limite do tipo")]
    public void AindaNaoMandouMandaEmQualquerRelogio(long agora)
    {
        Assert.That(TrackLimitsService.DeveReenviar(null, agora, 10_000), Is.True);
    }

    [Test]
    public void AcabouDeMandarEntaoEspera()
    {
        Assert.That(TrackLimitsService.DeveReenviar(1_000, 1_000, 10_000), Is.False);
        Assert.That(TrackLimitsService.DeveReenviar(1_000, 10_999, 10_000), Is.False);
    }

    [Test]
    public void PassadoOIntervaloMandaDeNovo()
    {
        Assert.That(TrackLimitsService.DeveReenviar(1_000, 11_000, 10_000), Is.True);
        Assert.That(TrackLimitsService.DeveReenviar(1_000, 60_000, 10_000), Is.True);
    }
}
