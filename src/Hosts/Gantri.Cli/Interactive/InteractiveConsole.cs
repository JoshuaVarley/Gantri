using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Workflows;
using Gantri.Cli.Infrastructure;
using Gantri.Cli.Interactive.Commands;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Gantri.Cli.Interactive;

/// <summary>
/// Main REPL loop for the Gantri interactive console.
/// Launched when <c>gantri</c> is invoked with no arguments.
/// </summary>
internal sealed class InteractiveConsole
{
    private readonly ConsoleContext _context;
    private readonly SlashCommandRouter _router;

    public InteractiveConsole(IServiceProvider serviceProvider)
    {
        var orchestrator = serviceProvider.GetRequiredService<IAgentOrchestrator>();
        var workflowEngine = serviceProvider.GetRequiredService<IWorkflowEngine>();
        var workerClient = serviceProvider.GetRequiredService<WorkerMcpClient>();
        var renderer = serviceProvider.GetRequiredService<ConsoleRenderer>();

        _context = new ConsoleContext(orchestrator, workflowEngine, workerClient, renderer);
        _router = serviceProvider.GetRequiredService<SlashCommandRouter>();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _context.Renderer.RenderWelcome();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _context.ExitRequested = true;
            cts.Cancel();
        };

        while (!_context.ExitRequested)
        {
            try
            {
                var prompt = _context.ActiveSession is not null
                    ? $"[cyan]{Markup.Escape(_context.ActiveAgentName ?? "agent")}>[/]"
                    : "[green]gantri>[/]";

                string input;
                try
                {
                    input = AnsiConsole.Prompt(
                        new TextPrompt<string>(prompt)
                            .AllowEmpty());
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    if (_context.ActiveSession is not null)
                        _context.Renderer.RenderInfo("Type a message or /help for commands.");
                    else
                        _context.Renderer.RenderInfo("Type /help for available commands, or /agent <name> to start chatting.");
                    continue;
                }

                if (input.StartsWith('/'))
                {
                    await HandleSlashCommandAsync(input, cts.Token);
                }
                else if (_context.ActiveSession is not null)
                {
                    await HandleAgentMessageAsync(input, cts.Token);
                }
                else
                {
                    _context.Renderer.RenderInfo("No active agent session. Use /agent <name> to start one, or /help for commands.");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _context.Renderer.RenderError(ex.Message);
            }
        }

        // Cleanup
        await _context.EndSessionAsync();
        _context.Renderer.RenderInfo("Goodbye!");

        return 0;
    }

    private async Task HandleSlashCommandAsync(string input, CancellationToken ct)
    {
        var (name, args) = SlashCommandRouter.Parse(input);

        if (string.IsNullOrEmpty(name))
        {
            _context.Renderer.RenderError("Invalid command. Type /help for available commands.");
            return;
        }

        if (_router.TryGetCommand(name, out var command) && command is not null)
        {
            await command.ExecuteAsync(args, _context, ct);
        }
        else
        {
            _context.Renderer.RenderError($"Unknown command '/{name}'. Type /help for available commands.");
        }
    }

    private async Task HandleAgentMessageAsync(string message, CancellationToken ct)
    {
        _context.MessageCount++;

        try
        {
            var tokens = _context.ActiveSession!.SendMessageStreamingAsync(message, ct);
            await _context.Renderer.RenderStreamingResponseAsync(tokens, ct);
        }
        catch (Exception ex)
        {
            _context.Renderer.RenderError($"Agent error: {ex.Message}");
        }
    }
}
