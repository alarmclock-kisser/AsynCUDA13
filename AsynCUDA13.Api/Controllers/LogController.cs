using AsynCUDA13.Shared.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    public class LogController : Controller
    {
        private readonly IRollingFileMemoryLogger _logger;

        public LogController(IRollingFileMemoryLogger logger)
        {
            this._logger = logger;
        }

        [HttpGet("log-lines")]
        public ActionResult<IEnumerable<string>> GetLogLines(int? nLastMax = null)
        {
            try
            {
                var lines = this._logger.GetLogLines();
                return this.Ok(lines.TakeLast(nLastMax ?? lines.Count));
            }
            catch
            {
                return this.Ok(Array.Empty<string>());
            }
        }

        [HttpGet("log-file")]
        public ActionResult GetLogFile()
        {
            try
            {
                var logLines = this._logger.GetLogLines();
                return this.File(System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, logLines)), "text/plain", "application.log");
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving log file",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpPost("log-comment")]
        public ActionResult LogComment([FromBody] string comment)
        {
            try
            {
                this._logger.Log(comment);
                return this.Ok();
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error logging comment",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

        [HttpDelete("log-clear")]
        public ActionResult ClearLog()
        {
            try
            {
                this._logger.ClearLogs();
                return this.Ok();
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error clearing log",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }

    }
}
