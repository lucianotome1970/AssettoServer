using AssettoServer.Server;

namespace AssettoServer.Tests;

/// <summary>
/// O nome que um carro de IA mostra.
///
/// <para>
/// O UPSTREAM IGNORAVA O DRIVERNAME da lista de inscritos: a IA era sempre
/// <c>"{NamePrefix} {SessionId}"</c>. Para um servidor de trafego isso basta --
/// "Traffic 7" --, mas num ensaio de transmissao o nome aparece na torre, na
/// placa do piloto e na barra de gap, e um grid de "Traffic" nao serve para
/// julgar enquadramento nenhum.
/// </para>
/// </summary>
public class NomeDaIaTests
{
    [Test]
    public void O_nome_da_lista_vence()
    {
        Assert.That(EntryCar.NomeDaIa("Luciano Tome", "Traffic", 7), Is.EqualTo("Luciano Tome"));
    }

    // CAI PARA O PREFIXO quando ninguem escreveu nada. E o comportamento do
    // upstream, e e o certo: nome em branco na torre e pior que "Traffic 7".
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Sem_nome_na_lista_usa_o_prefixo(string? daLista)
    {
        Assert.That(EntryCar.NomeDaIa(daLista, "Traffic", 7), Is.EqualTo("Traffic 7"));
    }

    [Test]
    public void O_session_id_entra_no_nome_do_prefixo()
    {
        // Sem o id, trinta e cinco carros de IA teriam o mesmo nome e a torre
        // viraria uma coluna de "Traffic".
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 0), Is.EqualTo("Traffic 0"));
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 34), Is.EqualTo("Traffic 34"));
    }

    [Test]
    public void Espaco_em_volta_do_nome_nao_vai_para_a_tela()
    {
        // O INI e editado a mao; espaco sobrando depois do '=' e rotina, e ele
        // desalinha o nome na torre.
        Assert.That(EntryCar.NomeDaIa("  Ana Oliveira  ", "Traffic", 3), Is.EqualTo("Ana Oliveira"));
    }
}
