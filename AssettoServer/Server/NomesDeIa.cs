using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace AssettoServer.Server;

/// <summary>
/// Os nomes dos pilotos de IA, lidos de <c>cfg/nomes_ia.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// POR QUE UM ARQUIVO, E NAO UMA LISTA NO YAML: quem edita isto e a liga, nao
/// quem mexe no servidor. Um nome por linha nao tem indentacao para errar, e um
/// erro aqui nao derruba o servidor -- ao contrario de um YAML mal recuado, que
/// impede a subida inteira por causa de um nome de piloto.
/// </para>
///
/// <para>
/// O NOME E SEMPRE O MESMO PARA A MESMA VAGA. Ele aparece na torre, na mesa de
/// corte e no replay; se mudasse a cada reinicio, o mesmo carro viraria outro
/// piloto no meio do fim de semana -- e quem narra acompanha por nome, nao por
/// numero de vaga. Por isso o sorteio e pelo indice de sessao, e nao aleatorio.
/// </para>
///
/// <para>
/// NAO VENCE O <c>DRIVERNAME</c> da lista de inscritos: quem escreveu um nome
/// la escreveu de proposito. Esta lista e so o que preenche o resto.
/// </para>
/// </remarks>
public sealed class NomesDeIa
{
    public const string NomeDoArquivo = "nomes_ia.txt";

    private readonly IReadOnlyList<string> _nomes;

    public NomesDeIa(IReadOnlyList<string> nomes) => _nomes = nomes;

    /// <summary>Quantos nomes a lista tem. Zero quer dizer "sem lista".</summary>
    public int Quantidade => _nomes.Count;

    /// <summary>
    /// A lista ja lida, por pasta.
    /// </summary>
    /// <remarks>
    /// UMA LEITURA POR SERVIDOR, e nao uma por carro: quem chama e o
    /// <c>AiInit</c> de CADA vaga, e sem isto um entry_list de 35 abriria o
    /// mesmo arquivo 35 vezes na subida. O conteudo e fixo enquanto o servidor
    /// roda -- trocar nome de IA e coisa de entre sessoes, nao de meio de
    /// corrida.
    /// </remarks>
    private static readonly Dictionary<string, NomesDeIa> Lidas = new();

    /// <summary>
    /// Le o arquivo. Faltando ou vazio, devolve uma lista vazia -- e ai o nome
    /// cai no prefixo, que e o comportamento que o servidor sempre teve.
    /// </summary>
    public static NomesDeIa Carregar(string pasta)
    {
        lock (Lidas)
        {
            if (Lidas.TryGetValue(pasta, out var ja)) return ja;
            var nova = Ler(pasta);
            Lidas[pasta] = nova;
            return nova;
        }
    }

    private static NomesDeIa Ler(string pasta)
    {
        var caminho = Path.Join(pasta, NomeDoArquivo);
        if (!File.Exists(caminho))
        {
            return new NomesDeIa(Array.Empty<string>());
        }

        try
        {
            return new NomesDeIa(Interpretar(File.ReadAllLines(caminho)));
        }
        catch (Exception ex)
        {
            // NAO DERRUBA O SERVIDOR. Um arquivo ilegivel custa o nome bonito,
            // nao a corrida: sem ele os carros voltam a "Piloto 7" e a sessao
            // acontece do mesmo jeito.
            Log.Warning(ex, "Nao consegui ler {Arquivo}; a IA fica com o nome padrao", caminho);
            return new NomesDeIa(Array.Empty<string>());
        }
    }

    /// <summary>
    /// Linhas em nomes: tira comentario, espaco e linha vazia.
    /// </summary>
    /// <remarks>
    /// SEPARADA DA LEITURA para ter teste sem tocar disco. O que pode dar
    /// errado aqui e exatamente o que um editor de texto produz sem avisar --
    /// BOM no comeco, espaco no fim, linha em branco no final do arquivo.
    /// </remarks>
    internal static List<string> Interpretar(IEnumerable<string> linhas)
    {
        var fora = new List<string>();
        foreach (var bruta in linhas)
        {
            // O BOM do UTF-8 gruda na PRIMEIRA linha e passaria despercebido:
            // o nome sairia com um caractere invisivel na frente, que so
            // aparece quando alguem tenta casar o nome com o cadastro.
            var linha = bruta.TrimStart('﻿').Trim();
            if (linha.Length == 0 || linha.StartsWith('#')) continue;
            fora.Add(linha);
        }
        return fora;
    }

    /// <summary>
    /// O nome da vaga, ou <c>null</c> quando nao ha lista.
    /// </summary>
    /// <remarks>
    /// COM MAIS VAGAS QUE NOMES a lista da a volta, e a repeticao ganha um
    /// numero: duas vagas com o nome identico na mesma pista fariam a torre
    /// mostrar o mesmo piloto em duas posicoes, e nem a mesa nem quem narra
    /// teriam como distinguir. Melhor um "Mika Virtanen II" feio do que dois
    /// Mika Virtanen.
    /// </remarks>
    public string? Para(byte sessionId)
    {
        if (_nomes.Count == 0) return null;

        var nome = _nomes[sessionId % _nomes.Count];
        var volta = sessionId / _nomes.Count;
        return volta == 0 ? nome : $"{nome} {Romano(volta + 1)}";
    }

    /// <summary>
    /// II, III, IV... Para numero de piloto repetido, nunca passa de um punhado.
    /// </summary>
    /// <remarks>
    /// ROMANO E NAO ARABE de proposito: "Mika Virtanen 2" se le como um numero
    /// de carro colado no nome. "Mika Virtanen II" se le como nome.
    /// </remarks>
    private static string Romano(int n) => n switch
    {
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        _ => n.ToString(),
    };
}
