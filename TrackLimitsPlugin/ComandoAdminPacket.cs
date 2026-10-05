using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Um comando de admin vindo do HUD, em texto, para o servidor executar.
///
/// <para>
/// TEXTO, E NÃO UM CÓDIGO POR COMANDO. O servidor já tem um interpretador de
/// comandos -- o mesmo que atende <c>/kick</c> no chat -- com os tipos, os
/// valores padrão e as mensagens de erro prontos. Mandando texto, a lista de
/// botões do HUD muda em Lua e nada aqui precisa ser recompilado; mandando um
/// código por comando, cada comando novo da liga custaria um build e uma
/// parada do servidor para trocar a DLL.
/// </para>
///
/// <para>
/// ISTO NÃO DÁ PODER NOVO A NINGUÉM, e esse é o ponto. O portão é
/// <c>ACTcpClient.IsAdministrator</c>, exatamente o mesmo que o chat usa: quem
/// pode mandar por aqui já podia digitar a mesma linha no chat. O canal só
/// troca digitar por clicar.
/// </para>
///
/// <para>
/// E NÃO PRECISA DE SENHA. <c>IsAdministrator</c> é concedido no handshake por
/// GUID, pelo <c>admins.txt</c> (<c>EntryCarManager</c> →
/// <c>IsAdminAsync(guid)</c>), antes de o piloto digitar coisa alguma. Era
/// justamente a senha que impedia esta tela de existir: <c>/admin &lt;senha&gt;</c>
/// no chat obrigaria a guardar a senha da liga em texto puro no disco de cada
/// comissário.
/// </para>
///
/// <para>
/// 96 CARACTERES é o teto. O maior comando da liga é um
/// <c>setcspweather &lt;clima&gt; &lt;duração&gt;</c>; o nome de piloto, que é o
/// que costuma ser longo, nem entra -- o HUD mira pela VAGA, que o
/// <c>ACClientTypeParser</c> aceita como número e que a torre já conhece de cor.
/// </para>
/// </summary>
[OnlineEvent(Key = "comandoAdmin")]
public class ComandoAdminPacket : OnlineEvent<ComandoAdminPacket>
{
    /// <summary>A linha de comando, sem a barra.</summary>
    [OnlineEventField(Name = "texto", Size = 96)]
    public string Texto = "";
}
