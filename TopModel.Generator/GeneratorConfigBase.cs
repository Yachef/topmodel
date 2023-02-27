﻿using System.Text.RegularExpressions;
using TopModel.Core;

namespace TopModel.Generator;

public abstract class GeneratorConfigBase
{
#nullable disable

    /// <summary>
    /// Racine du répertoire de génération.
    /// </summary>
    public string OutputDirectory { get; set; }

    /// <summary>
    /// Tags du générateur.
    /// </summary>
    public IList<string> Tags { get; set; }
#nullable enable

    /// <summary>
    /// Variables globales du générateur.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Variables par tag du générateur.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> TagVariables { get; set; } = new();

    public IEnumerable<string> TagVariableNames => TagVariables.Values.SelectMany(v => v.Keys).Distinct();

    public IEnumerable<string> GlobalVariableNames => Variables.Select(v => v.Key).Except(TagVariableNames).Distinct();

    /// <summary>
    /// Propriétés qui supportent la variable "app".
    /// </summary>
    public virtual string[] PropertiesWithAppVariableSupport => Array.Empty<string>();

    /// <summary>
    /// Propriétés qui supportent la variable "module".
    /// </summary>
    public virtual string[] PropertiesWithModuleVariableSupport => Array.Empty<string>();

    /// <summary>
    /// Propriétés qui supportent la variable "lang".
    /// </summary>
    public virtual string[] PropertiesWithLangVariableSupport => Array.Empty<string>();

    /// <summary>
    /// Propriétés qui supportent les variables par tag de la configuration courante.
    /// </summary>
    public virtual string[] PropertiesWithTagVariableSupport => Array.Empty<string>();

    /// <summary>
    /// Résout toutes les variables pour une valeur donnée.
    /// </summary>
    /// <param name="value">Valeur.</param>
    /// <param name="tag">Tag.</param>
    /// <param name="app">App.</param>
    /// <param name="module">Module.</param>
    /// <param name="lang">Lang.</param>
    /// <param name="trimBeforeApp">Supprime tout ce qui précède le premier {app}.</param>
    /// <returns>La valeur avec les variables résolues.</returns>
    public virtual string ResolveVariables(string value, string? tag = null, string? app = null, string? module = null, string? lang = null, bool trimBeforeApp = false)
    {
        var result = value;

        if (tag != null)
        {
            result = ResolveTagVariables(result, tag);
        }

        if (app != null)
        {
            if (trimBeforeApp)
            {
                result = result[Math.Max(0, result.IndexOf("{app}"))..];
            }

            result = ReplaceVariable(result, "app", app);
        }

        if (module != null)
        {
            result = ReplaceVariable(result, "module", module);
        }

        if (lang != null)
        {
            result = ReplaceVariable(result, "lang", lang);
        }

        return result;
    }

    /// <summary>
    /// Initialise les variables globales, et par tag manquantes.
    /// </summary>
    /// <param name="number">Numéro du générateur.</param>
    internal void InitVariables(int number)
    {
        // Si on a défini au moins une variable par tag, alors on s'assure qu'elle est définie pour tous les tags (et on y met "" si ce n'est pas une variable globale).
        if (TagVariableNames.Any())
        {
            foreach (var tag in Tags)
            {
                if (!TagVariables.ContainsKey(tag))
                {
                    TagVariables[tag] = new();
                }
            }

            foreach (var variables in TagVariables.Values)
            {
                foreach (var varName in TagVariableNames)
                {
                    if (!variables.ContainsKey(varName))
                    {
                        Variables.TryGetValue(varName, out var globalVariable);
                        variables[varName] = globalVariable ?? string.Empty;
                    }
                }
            }
        }

        var hasMissingVar = false;

        foreach (var property in GetType().GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            var value = (string?)property.GetValue(this);
            if (value != null)
            {
                foreach (var varName in GlobalVariableNames)
                {
                    value = ReplaceVariable(value, varName, Variables[varName]);
                }

                property.SetValue(this, value);

                foreach (var match in Regex.Matches(value, @"\{([$a-zA-Z0-9_-]+)(:\w+)?\}").Cast<Match>())
                {
                    var varName = match.Groups[1].Value;
                    if (varName == "app" || varName == "module" || varName == "lang")
                    {
                        var supportedProperties = varName switch
                        {
                            "app" => PropertiesWithAppVariableSupport,
                            "module" => PropertiesWithModuleVariableSupport,
                            "lang" => PropertiesWithLangVariableSupport,
                            _ => null!
                        };

                        if (!supportedProperties.Contains(property.Name))
                        {
                            hasMissingVar = true;
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"/!\\ {{{GetType().Name}[{number}].{property.Name}}} - La variable '{{{varName}}}' n'est pas supportée par cette propriété.");
                            Console.ForegroundColor = ConsoleColor.Gray;
                        }

                        continue;
                    }

                    var hasTagSupport = PropertiesWithTagVariableSupport.Contains(property.Name);

                    if (!hasTagSupport)
                    {
                        hasMissingVar = true;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"/!\\ {{{GetType().Name}[{number}].{property.Name}}} - La variable globale '{{{varName}}}' n'est pas définie pour ce générateur.");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                    else if (!TagVariableNames.Contains(varName))
                    {
                        hasMissingVar = true;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"/!\\ {{{GetType().Name}[{number}].{property.Name}}} - La variable '{{{varName}}}' n'est pas définie pour ce générateur.");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                }
            }
        }

        foreach (var tagVariables in TagVariables.Values)
        {
            foreach (var tagVarName in tagVariables.Keys)
            {
                foreach (var varName in GlobalVariableNames)
                {
                    tagVariables[tagVarName] = ReplaceVariable(tagVariables[tagVarName], varName, Variables[varName]);
                }
            }
        }

        if (hasMissingVar)
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Résout les variables de tag dans un chaîne de caractère.
    /// </summary>
    /// <param name="value">Chaîne de caractères.</param>
    /// <param name="tag">Nom du tag.</param>
    /// <returns>Value avec les variables remplacées..</returns>
    protected virtual string ResolveTagVariables(string value, string tag)
    {
        if (TagVariables.TryGetValue(tag, out var tagVariables))
        {
            foreach (var (varName, varValue) in tagVariables)
            {
                value = ReplaceVariable(value, varName, varValue);
            }
        }

        return value;
    }

    private static string ReplaceVariable(string value, string varName, string varValue)
    {
        return Regex.Replace(value, $"\\{{{varName}(:\\w+)?\\}}", m => m.Value.Trim('{', '}').GetTransformation()(varValue));
    }
}