using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace SondaFisicaPlugin;

/// <summary>
/// Configuracao da sonda. Ela nao tem regra de corrida nenhuma: e ferramenta
/// de medicao, e sai do ar assim que as tres perguntas forem respondidas.
/// </summary>
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class SondaFisicaConfiguration
{
    [YamlMember(Description =
        "Quantos segundos de amostragem (rpm e velocidade a cada 0,5 s) o cliente manda "
        + "depois de cada mudanca de restritor. E com esse traco que se mede a ESCALA do "
        + "restritor, porque nao existe getter de potencia: a diferenca de aceleracao entre "
        + "duas passadas iguais com restritores diferentes e a unica medida disponivel")]
    public float SegundosDeTraco { get; init; } = 12f;
}
