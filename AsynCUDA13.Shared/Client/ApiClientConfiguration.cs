using AsynCUDA13.Shared.Localization;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.Client
{
    public class ApiClientConfiguration
    {
        public string ApiBaseUrl { get; set; } = "https://localhost:7186";
        public int LogLevel { get; set; } = 4;



        public ApiClientConfiguration()
        {

        }
    }
}
