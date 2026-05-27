using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Tests.ApiFirst;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGeodeFactory();
    }
}
