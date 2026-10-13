
namespace AssettoServer.Shared.Model;

public class EntryCarResult
{
    public ulong Guid { get; set; } = 0;
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public string NationCode { get; set; } = "";
    public uint BestLap { get; set; } = 999999999;
    public uint NumLaps { get; set; } = 0;
    public uint TotalTime { get; set; } = 0;
    public uint LastLap { get; set; } = 999999999;
    public bool HasCompletedLastLap { get; set; } = false;
    public uint RacePos { get; set; } = 0;

    /// <summary>
    /// Os setores da ULTIMA volta fechada, em milissegundos.
    ///
    /// <para>
    /// O cliente SEMPRE mandou isto -- <c>LapCompletedIncoming.Splits</c> --,
    /// e o servidor lia e jogava fora. Guardar e barato: e o unico lugar do
    /// servidor que sabe onde o piloto perdeu tempo, e sem isso a mesa e a
    /// tela da pista so conseguem mostrar o total da volta.
    /// </para>
    ///
    /// <para>
    /// Vazio enquanto ninguem fechou volta, e nao um vetor de zeros: zero e um
    /// tempo de setor possivel de ler como "voou", e quem le precisa separar
    /// "nao sei" de "foi rapido".
    /// </para>
    /// </summary>
    public uint[] LastSplits { get; set; } = [];

    public EntryCarResult(IClient? client)
    {
        if (client == null) return;
        
        Guid = client.Guid;
        Name = client.Name ?? "";
        Team = client.Team ?? "";
        NationCode = client.NationCode ?? "";
    }
}
