using AsynCUDA13.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    public class LogController : Controller
    {
        [HttpGet("log-lines")]
        public ActionResult<IEnumerable<string>> GetLogLines(int? nLastMax = null)
        {
            try
            {
                return this.Ok(StaticLogger.LogEntries.OrderBy(e => e.Key).TakeLast(nLastMax ?? StaticLogger.LogEntries.Count).Select(e => e.Value));
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
                var logLines = StaticLogger.LogEntries.Values;
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
                StaticLogger.Log(comment);
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
                StaticLogger.ClearLogs();
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
