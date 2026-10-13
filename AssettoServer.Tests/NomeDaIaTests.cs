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
///
/// <para>
/// SAO TRES DEGRAUS, e a ORDEM e o que este teste protege: o nome escrito na
/// lista de inscritos, depois a lista de <c>cfg/nomes_ia.txt</c>, e so entao o
/// prefixo. Invertendo os dois primeiros, um nome escrito a mao seria
/// atropelado por um sorteado -- e ninguem perceberia, porque os dois sao
/// nomes plausiveis.
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

    // ───────────────────── a lista de nomes ─────────────────────

    private static NomesDeIa Lista(params string[] nomes) => new(nomes);

    [Test]
    public void A_lista_entra_no_lugar_do_prefixo()
    {
        var l = Lista("Mika Virtanen", "Lorenzo Baldini", "Kenji Matsuda");
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 0, l), Is.EqualTo("Mika Virtanen"));
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 2, l), Is.EqualTo("Kenji Matsuda"));
    }

    [Test]
    public void O_nome_escrito_a_mao_vence_a_lista()
    {
        // A ORDEM DOS DEGRAUS. Quem escreveu "Luciano Tome" no entry_list
        // escreveu de proposito; a lista so preenche o que ninguem nomeou.
        var l = Lista("Mika Virtanen");
        Assert.That(EntryCar.NomeDaIa("Luciano Tome", "Traffic", 0, l), Is.EqualTo("Luciano Tome"));
    }

    [Test]
    public void Sem_lista_continua_caindo_no_prefixo()
    {
        // O servidor de trafego nao tem nomes_ia.txt, e tem que seguir igual.
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 7, Lista()), Is.EqualTo("Traffic 7"));
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 7, null), Is.EqualTo("Traffic 7"));
    }

    [Test]
    public void A_mesma_vaga_recebe_sempre_o_mesmo_piloto()
    {
        // NAO E DETALHE: o nome aparece na torre, na mesa e no replay. Mudando
        // a cada subida, o mesmo carro viraria outro piloto no meio do fim de
        // semana -- e quem narra acompanha por nome, nao por numero de vaga.
        var l = Lista("Mika Virtanen", "Lorenzo Baldini");
        var primeira = EntryCar.NomeDaIa(null, "Traffic", 1, l);
        var segunda = EntryCar.NomeDaIa(null, "Traffic", 1, Lista("Mika Virtanen", "Lorenzo Baldini"));
        Assert.That(segunda, Is.EqualTo(primeira));
    }

    [Test]
    public void Mais_vagas_que_nomes_nao_repete_nome_identico()
    {
        // Dando a volta, a torre mostraria dois pilotos iguais em posicoes
        // diferentes -- e nem a mesa nem quem narra teriam como distinguir.
        var l = Lista("Mika Virtanen", "Lorenzo Baldini");
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 0, l), Is.EqualTo("Mika Virtanen"));
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 2, l), Is.EqualTo("Mika Virtanen II"));
        Assert.That(EntryCar.NomeDaIa(null, "Traffic", 4, l), Is.EqualTo("Mika Virtanen III"));
    }

    // ───────────────────── o arquivo ─────────────────────

    [Test]
    public void Comentario_e_linha_vazia_nao_viram_piloto()
    {
        var nomes = NomesDeIa.Interpretar(new[]
        {
            "# Brasil", "", "Rafael Andrade", "   ", "# Italia", "Lorenzo Baldini",
        });
        Assert.That(nomes, Is.EqualTo(new[] { "Rafael Andrade", "Lorenzo Baldini" }));
    }

    [Test]
    public void O_BOM_do_editor_nao_gruda_no_primeiro_nome()
    {
        // O Bloco de Notas salva UTF-8 com BOM, e ele cai na PRIMEIRA linha.
        // Sem tirar, o nome sai com um caractere invisivel na frente -- que so
        // aparece quando alguem tenta casar esse nome com o cadastro.
        var nomes = NomesDeIa.Interpretar(new[] { "﻿Rafael Andrade" });
        Assert.That(nomes, Is.EqualTo(new[] { "Rafael Andrade" }));
    }

    [Test]
    public void Espaco_no_fim_da_linha_nao_entra_no_nome()
    {
        var nomes = NomesDeIa.Interpretar(new[] { "  Mika Virtanen  " });
        Assert.That(nomes, Is.EqualTo(new[] { "Mika Virtanen" }));
    }
}
