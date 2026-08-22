using Azure;
using Azure.Identity;

namespace FoundryWorkshop.Shared;

public static class LabHost
{
    public static async Task<int> RunAsync(
        string labName,
        string[] args,
        Func<WorkshopContext, Task> run,
        params string[] requiredVariables)
    {
        Console.WriteLine($"Microsoft Foundry Workshop - {labName}");
        Console.WriteLine(new string('=', Math.Min(72, labName.Length + 31)));

        try
        {
            using var context = new WorkshopContext(labName, args);
            if (context.HasFlag("--check"))
            {
                PrintConfiguration(context.Config, requiredVariables);
                return 0;
            }

            foreach (var variable in requiredVariables)
            {
                context.Config.Require(variable);
            }

            await run(context);
            return 0;
        }
        catch (WorkshopConfigurationException ex)
        {
            Console.Error.WriteLine($"Configuration: {ex.Message}");
            Console.Error.WriteLine("Run this lab with --check to inspect its requirements.");
            return 2;
        }
        catch (AuthenticationFailedException ex)
        {
            Console.Error.WriteLine($"Authentication failed: {ex.Message}");
            Console.Error.WriteLine("Run 'az login' and confirm your identity has the required Foundry/Azure role.");
            return 3;
        }
        catch (RequestFailedException ex)
        {
            Console.Error.WriteLine($"Azure request failed ({ex.Status}, {ex.ErrorCode}): {ex.Message}");
            return 4;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"HTTP request failed: {ex.Message}");
            return 5;
        }
    }

    private static void PrintConfiguration(WorkshopConfig config, IEnumerable<string> requiredVariables)
    {
        var required = requiredVariables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Console.WriteLine("Configuration check only; no Azure calls were made.");
        foreach (var name in required)
        {
            Console.WriteLine($"  {(config.IsConfigured(name) ? "[ready]" : "[missing]")} {name}");
        }

        if (required.Length == 0)
        {
            Console.WriteLine("  This mode has no required Azure configuration.");
        }
    }
}
