using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Anuncia a TODOS que dois carros se tocaram, e com que violência.
///
/// <para>
/// POR QUE VEM DO SERVIDOR, e não do Lua: a detecção no cliente não funciona
/// para isto. <c>ac.onCarCollision</c> entrega só o índice do PRÓPRIO carro,
/// dispara uma vez por carro e ainda roda dentro de replay — numa sessão
/// inteira de teste ela ficou em <c>0 detectados</c>. O servidor recebe
/// <c>ClientEventType.CollisionWithCar</c> de quem bateu, com quem levou e a
/// velocidade relativa, e enxerga os 35 carros, não só os que estão perto de
/// alguém.
/// </para>
///
/// <para>
/// A VELOCIDADE VAI JUNTO porque é ela que separa um encosto de um acidente.
/// Sem o número, a mesa teria que chamar de "batida" tanto o toque de 3 km/h na
/// fila do box quanto a pancada de 90 km/h na freada — e quem narra perderia
/// tempo conferindo o replay de todo encosto.
/// </para>
///
/// <para>
/// NÃO JULGA CULPA. Quem reportou o evento é <see cref="Carro"/> e quem levou é
/// <see cref="Outro"/>, e isso é a ordem em que o AC entrega, não um veredito:
/// o jogo manda o evento do cliente que detectou o contato. Transformar isso em
/// "causador" seria inventar um juiz a partir de quem tem menos ping.
/// </para>
/// </summary>
[OnlineEvent(Key = "colisao")]
public class ColisaoPacket : OnlineEvent<ColisaoPacket>
{
    /// <summary>A vaga de quem reportou o contato.</summary>
    [OnlineEventField(Name = "carro")]
    public byte Carro;

    /// <summary>
    /// A vaga do outro carro, ou <see cref="Ambiente"/> quando foi muro.
    /// </summary>
    /// <remarks>
    /// MURO TAMBÉM VAI, e não é desperdício: um carro sozinho na barreira é
    /// exatamente o momento que a transmissão quer rever, e sem este aviso ele
    /// só apareceria depois, como um tempo que piorou sem explicação.
    /// </remarks>
    [OnlineEventField(Name = "outro")]
    public byte Outro;

    /// <summary>Velocidade relativa do contato, em km/h.</summary>
    [OnlineEventField(Name = "kmh")]
    public float Kmh;

    /// <summary>Valor de <see cref="Outro"/> quando o contato foi com o cenário.</summary>
    public const byte Ambiente = 255;
}
