using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace SondaFisicaPlugin;

/// <summary>
/// Sonda de medicao: descobre se um script ENVIADO PELO SERVIDOR pode alterar
/// a fisica do carro, e com que escala.
///
/// <para>
/// PARA QUE ELA EXISTE. O push-to-pass do nosso carro hoje e um turbo
/// POSTICO: o `[TURBO_0]` do `engine.ini` existe so para o `[PUSH_TO_PASS]`
/// funcionar, e por causa dele a curva de potencia esta dividida por 1,2. Pior
/// que a feiura: a quantidade de acionamentos e a duracao moram no
/// `data.acd`, que e arquivo do CLIENTE -- qualquer piloto edita o seu.
/// </para>
///
/// <para>
/// O SDK do script de servidor tem `physics.setCarRestrictor(carIndex,
/// restrictor)`, documentado como "Sets car restrictor. Does not invalidate
/// lap". Com ele o desenho vira: o carro roda restringido o tempo todo, e o
/// push e o SERVIDOR tirando o restritor por alguns segundos. Nada para
/// editar no pacote do carro, porque a potencia extra nao esta la.
/// </para>
///
/// <para>
/// MAS ISSO DEPENDE DE UMA COISA QUE A DOCUMENTACAO NAO GARANTE: que
/// `physics.allowed()` seja verdadeiro num script enviado pelo servidor. Se
/// nao for, "so `physics.raycastTrack()` fica disponivel" e o desenho inteiro
/// cai. Esta sonda responde isso em uma sessao, em vez de descobrirmos depois
/// de escrever o sistema.
/// </para>
///
/// <para>
/// ELA E DESCARTAVEL. Respondidas as tres perguntas -- se e permitido, qual a
/// escala do restritor e se a troca e instantanea --, este plugin sai do
/// `EnablePlugins` e do repositorio.
/// </para>
/// </summary>
public class SondaFisicaService : BackgroundService
{
    private readonly SondaFisicaConfiguration _configuracao;
    private readonly EntryCarManager _carros;

    public SondaFisicaService(SondaFisicaConfiguration configuracao,
        CSPServerScriptProvider provedorDeScript,
        CSPClientMessageTypeManager mensagens,
        EntryCarManager carros)
    {
        _configuracao = configuracao;
        _carros = carros;

        var caminho = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "lua", "sonda_fisica.lua");
        using var leitor = new StreamReader(caminho);
        provedorDeScript.AddScript(leitor.ReadToEnd(), "sonda_fisica.lua");

        mensagens.RegisterOnlineEvent<SondaRelatorioPacket>(AoReceberRelatorio);

        Log.Information("SondaFisica: script enviado aos clientes. Use /sonda <valor> para medir");
    }

    /// <summary>
    /// Manda o cliente aplicar um restritor. So o comando de administrador
    /// chama isto.
    /// </summary>
    public void Pedir(ACTcpClient cliente, float valor)
    {
        cliente.SendPacket(new SondaComandoPacket
        {
            Valor = valor,
            Segundos = _configuracao.SegundosDeTraco,
        });
        Log.Information("SondaFisica: pedido restritor {Valor} para {Piloto}", valor, cliente.Name);
    }

    private void AoReceberRelatorio(ACTcpClient cliente, SondaRelatorioPacket pacote)
    {
        // O relatorio da CHEGADA e o que responde a pergunta que decide tudo,
        // entao ele sobe como aviso e os tracos ficam em informacao.
        if (pacote.Nota == "traco")
        {
            Log.Information("SondaFisica[{Piloto}] restritor={Restritor} rpm={Rpm:F0} kmh={Kmh:F1}",
                cliente.Name, pacote.Restritor, pacote.Rpm, pacote.Kmh);
            return;
        }

        Log.Warning("SondaFisica[{Piloto}] {Nota} | physics.allowed={Permitido} | CSP={Versao} | "
            + "restritor={Restritor} rpm={Rpm:F0} kmh={Kmh:F1}",
            cliente.Name, pacote.Nota, pacote.Permitido, pacote.VersaoCsp,
            pacote.Restritor, pacote.Rpm, pacote.Kmh);

        if (pacote.Nota == "na chegada" && !pacote.Permitido)
        {
            Log.Error("SondaFisica: physics.allowed() = FALSE para {Piloto}. Um script enviado "
                + "pelo servidor NAO pode alterar a fisica neste cliente -- o push-to-pass pelo "
                + "servidor nao e possivel por este caminho", cliente.Name);
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
