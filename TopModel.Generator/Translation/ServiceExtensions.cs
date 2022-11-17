﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TopModel.Core;
using static TopModel.Utils.ModelUtils;

namespace TopModel.Generator.Translation;

public static class ServiceExtensions
{
    public static IServiceCollection AddTranslationOut(this IServiceCollection services, string dn, IEnumerable<TranslationConfig>? configs)
    {
        if (configs != null)
        {
            for (var i = 0; i < configs.Count(); i++)
            {
                var config = configs.ElementAt(i);
                var number = i + 1;

                CombinePath(dn, config, c => c.OutputDirectory);
                services
                    .AddSingleton<IModelWatcher>(p =>
                        new TranslationOutGenerator(p.GetRequiredService<ILogger<TranslationOutGenerator>>(), config, p.GetRequiredService<TranslationStore>()));

            }
        }

        return services;
    }
}
