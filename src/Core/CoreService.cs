using System;
using System.Threading.Tasks;
using Grpc.Core;
using VoiceStudio.IPC;

namespace VoiceStudio.CoreRuntime
{
    public class CoreService : Core.CoreBase
    {
        private readonly JobRunner _runner;
        public CoreService(JobRunner runner) { _runner = runner; }

        public override Task<HealthReply> Health(Empty request, ServerCallContext context)
            => Task.FromResult(new HealthReply { Status = "OK", Version = "0.1.0" });

        public override async Task<RunResponse> Run(RunRequest request, ServerCallContext context)
        {
            var job = request.Job;
            try
            {
                var (ok, code, message, outputs) = await _runner.RunAsync(job);
                var resp = new RunResponse { Status = ok ? "ok" : "error", Code = code ?? "", Message = message ?? "" };
                if (outputs != null) resp.Outputs.AddRange(outputs);
                return resp;
            }
            catch (Exception ex)
            {
                return new RunResponse { Status = "error", Code = "E_UNHANDLED", Message = ex.Message };
            }
        }
    }
}
