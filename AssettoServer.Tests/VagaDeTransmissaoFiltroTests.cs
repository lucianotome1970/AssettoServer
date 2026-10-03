using AssettoServer.Server;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;

namespace AssettoServer.Tests;

/// <summary>
/// O filtro que mantem as listas do servidor do tamanho da lista do cliente.
///
/// <para>
/// O QUE ESTE TESTE PROTEGE NAO E UM CAMPO, E UMA IGUALDADE. O cliente honra o
/// bit de espectador e tira o carro da lista dele; a partir dai TODA lista de
/// carros que o servidor manda tem que ter o mesmo tamanho. Duas das tres --
/// <c>CurrentSessionUpdate</c> e <c>RaceOver</c> -- nem contador tem: o cliente
/// deduz a quantidade pelo que sobra do pacote e le o campo seguinte logo
/// depois, entao um carro a mais nao estoura um laco, DESLOCA o proximo campo.
/// </para>
///
/// <para>
/// Medido em 03/10/2026, antes do filtro existir: com o bit ligado e mais nada
/// mudado, todo cliente que conectava morria em segundos, em qualquer vaga.
/// </para>
/// </summary>
public class VagaDeTransmissaoFiltroTests
{
    /// <summary>Carro de mentira: a interface tem 8 membros e so um importa aqui.</summary>
    private sealed class CarroFalso(byte sessionId, bool espectador) : IEntryCar<IClient>
    {
        public byte SessionId { get; } = sessionId;
        public bool IsSpectator { get; } = espectador;
        public string Model => "sr_clio_cup";
        public string Skin => "";
        public CarStatus Status { get; } = new();
        public IClient? Client => null;
        public bool AiControlled => false;
        public string? AiName => null;
    }

    private static List<IEntryCar<IClient>> Grid(params byte[] espectadores)
    {
        var carros = new List<IEntryCar<IClient>>();
        for (byte i = 0; i < 35; i++) carros.Add(new CarroFalso(i, espectadores.Contains(i)));
        return carros;
    }

    private static Dictionary<byte, EntryCarResult> Resultados()
    {
        var d = new Dictionary<byte, EntryCarResult>();
        for (byte i = 0; i < 35; i++) d.Add(i, new EntryCarResult(null));
        return d;
    }

    [Test]
    public void OGridPerdeAVagaDeTransmissao()
    {
        var saida = VagaDeTransmissao.SemEspectadores(Grid(34)).ToList();

        Assert.That(saida, Has.Count.EqualTo(34));
        Assert.That(saida.Any(c => c.SessionId == 34), Is.False, "a vaga 34 nao devia ir para o fio");
        // A ORDEM IMPORTA: o pacote escreve um session id por posicao de grid, sem
        // contador. Reordenar aqui mudaria o grid de largada em silencio.
        Assert.That(saida.Select(c => c.SessionId), Is.EqualTo(Enumerable.Range(0, 34).Select(i => (byte)i)));
    }

    [Test]
    public void SemVagaDeTransmissaoNadaMuda()
    {
        Assert.That(VagaDeTransmissao.SemEspectadores(Grid()).Count(), Is.EqualTo(35));
    }

    [Test]
    public void OResultadoFinalPerdeAVagaDeTransmissao()
    {
        var saida = VagaDeTransmissao.SemEspectadores(Resultados(), id => id == 34);

        Assert.That(saida, Has.Count.EqualTo(34));
        Assert.That(saida.ContainsKey(34), Is.False);
        Assert.That(saida.ContainsKey(33), Is.True);
    }

    // A IGUALDADE, QUE E O QUE O CLIENTE EXIGE. Nao basta cada filtro funcionar
    // sozinho: os dois caminhos -- carros e resultados -- tem que chegar ao mesmo
    // numero, senao uma lista fica maior que a outra e o desalinhamento volta por
    // outra porta.
    [TestCase(new byte[] { }, 35)]
    [TestCase(new byte[] { 34 }, 34)]
    [TestCase(new byte[] { 0 }, 34)]
    [TestCase(new byte[] { 0, 17, 34 }, 32)]
    public void OsDoisCaminhosChegamAoMesmoNumero(byte[] espectadores, int esperado)
    {
        var porCarro = VagaDeTransmissao.SemEspectadores(Grid(espectadores)).Count();
        var porResultado = VagaDeTransmissao.SemEspectadores(Resultados(), espectadores.Contains).Count;

        Assert.That(porCarro, Is.EqualTo(esperado));
        Assert.That(porResultado, Is.EqualTo(esperado));
    }

    // TODAS ESPECTADORAS e caso degenerado, mas se alguem gerar uma lista assim o
    // servidor tem que mandar lista vazia, nao a lista inteira.
    [Test]
    public void GridInteiroDeEspectadoresVaiVazio()
    {
        var todas = Enumerable.Range(0, 35).Select(i => (byte)i).ToArray();
        Assert.That(VagaDeTransmissao.SemEspectadores(Grid(todas)), Is.Empty);
        Assert.That(VagaDeTransmissao.SemEspectadores(Resultados(), _ => true), Is.Empty);
    }

    // ----------------------------------------------------------------- "exceto"

    // A REGRA QUE CUSTOU O SEGUNDO CRASH. O cliente descarta o carro marcado MESMO
    // QUANDO O CARRO E ELE PROPRIO: quem entrou na vaga 34 morreu no primeiro pacote
    // endereçado a si. Entao cada cliente recebe uma visao onde faltam as OUTRAS
    // vagas de transmissao, nunca a dele.
    [Test]
    public void QuemOcupaAVagaEnxergaOProprioCarro()
    {
        var saida = VagaDeTransmissao.SemEspectadores(Grid(34), exceto: 34).ToList();

        Assert.That(saida, Has.Count.EqualTo(35));
        Assert.That(saida.Any(c => c.SessionId == 34), Is.True);
    }

    [Test]
    public void MasNaoEnxergaAsOUTRASVagasDeTransmissao()
    {
        var saida = VagaDeTransmissao.SemEspectadores(Grid(17, 34), exceto: 34).ToList();

        Assert.That(saida, Has.Count.EqualTo(34));
        Assert.That(saida.Any(c => c.SessionId == 34), Is.True, "a propria fica");
        Assert.That(saida.Any(c => c.SessionId == 17), Is.False, "a alheia sai");
    }

    [Test]
    public void OResultadoSegueAMesmaRegra()
    {
        var espectadores = new byte[] { 17, 34 };
        var minha = VagaDeTransmissao.SemEspectadores(Resultados(), espectadores.Contains, exceto: 34);
        var dosOutros = VagaDeTransmissao.SemEspectadores(Resultados(), espectadores.Contains);

        Assert.That(minha.ContainsKey(34), Is.True);
        Assert.That(minha.ContainsKey(17), Is.False);
        Assert.That(dosOutros.ContainsKey(34), Is.False);
        Assert.That(minha, Has.Count.EqualTo(dosOutros.Count + 1));
    }

    // A IGUALDADE, AGORA POR DESTINATARIO. Para CADA cliente, os dois caminhos --
    // carros e resultados -- tem que chegar ao mesmo numero. E o que o cliente dele
    // exige; nao existe um numero certo para todos.
    [TestCase((byte)0)]
    [TestCase((byte)17)]
    [TestCase((byte)34)]
    public void CadaDestinatarioVeOsDoisCaminhosDoMesmoTamanho(byte destinatario)
    {
        var espectadores = new byte[] { 17, 34 };

        var porCarro = VagaDeTransmissao.SemEspectadores(Grid(espectadores), destinatario).Count();
        var porResultado = VagaDeTransmissao
            .SemEspectadores(Resultados(), espectadores.Contains, destinatario).Count;

        Assert.That(porCarro, Is.EqualTo(porResultado));
        // 33 para quem nao e espectador, 34 para quem e (ve o proprio carro).
        Assert.That(porCarro, Is.EqualTo(espectadores.Contains(destinatario) ? 34 : 33));
    }

    // SEM DESTINATARIO o comportamento antigo continua: ninguem e poupado. E o que
    // vale para listas que nao sao de um cliente so.
    [Test]
    public void SemDestinatarioNinguemEPoupado()
    {
        Assert.That(VagaDeTransmissao.SemEspectadores(Grid(34)).Count(), Is.EqualTo(34));
    }
}
