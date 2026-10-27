using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace SondaFisicaPlugin;

public class SondaFisicaModule : AssettoServerModule<SondaFisicaConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SondaFisicaService>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
