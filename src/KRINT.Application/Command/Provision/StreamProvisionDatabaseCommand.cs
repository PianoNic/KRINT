using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Mediator;
using KRINT.Application.Dtos.Provision;

namespace KRINT.Application.Command.Provision
{
    /// <summary>
    /// The same provision as <see cref="ProvisionDatabaseCommand"/>, reported step by step. An
    /// image pull alone can take minutes; a client watching a spinner cannot tell that from a
    /// hang, and this is what lets the wizard say what it is waiting for.
    /// </summary>
    public record StreamProvisionDatabaseCommand(ProvisionRequestDto Request) : IStreamCommand<ProvisionProgressDto>;

    public class StreamProvisionDatabaseCommandHandler(IMediator mediator)
        : IStreamCommandHandler<StreamProvisionDatabaseCommand, ProvisionProgressDto>
    {
        public async IAsyncEnumerable<ProvisionProgressDto> Handle(
            StreamProvisionDatabaseCommand command,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<ProvisionProgressDto>(new UnboundedChannelOptions { SingleReader = true });
            var progress = new ChannelProgress(channel.Writer);

            // The provision runs to completion on its own task while this method drains the
            // channel; the channel's completion is what ends the stream, so nothing is lost when
            // the last step and the result arrive back to back.
            var run = Task.Run(async () =>
            {
                try
                {
                    var result = await mediator.Send(new ProvisionDatabaseCommand(command.Request, progress), cancellationToken);
                    channel.Writer.TryWrite(new ProvisionProgressDto { Status = "done", Message = "Instance ready", Result = result });
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    channel.Writer.TryWrite(new ProvisionProgressDto { Status = "failed", Message = "Provisioning failed", Error = ex.Message });
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }, CancellationToken.None);

            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;

            await run;
        }

        /// <summary>Writes each report straight into the channel, in order, on the reporting
        /// thread. <c>Progress&lt;T&gt;</c> would post through a synchronization context and
        /// reorder or drop nothing, but it would also hand the callback to the pool and lose the
        /// ordering guarantee the step list relies on.</summary>
        private sealed class ChannelProgress(ChannelWriter<ProvisionProgressDto> writer) : IProgress<string>
        {
            public void Report(string value) =>
                writer.TryWrite(new ProvisionProgressDto { Status = "running", Message = value });
        }
    }
}
