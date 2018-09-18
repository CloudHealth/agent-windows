using System;
using System.IO;
using System.Net;
using RestSharp;

namespace CloudHealth
{
    public class API
    {
        public class AsyncRequestHandle
        {
            private readonly RestRequestAsyncHandle handle;

            internal AsyncRequestHandle(RestRequestAsyncHandle handle)
            {
                this.handle = handle;
            }

            public void Abort()
            {
                handle.Abort();
            }
        }

        private readonly RestClient client;
        private readonly string accessToken;
        private readonly Logger logger;

        public API(AgentConfig config, string accessToken)
        {
            logger = new Logger();
            this.accessToken = accessToken;
            client = new RestClient(config.BaseURL);
            client.Proxy = config;
        }

        public void ReportError(string message, string level, object details)
        {
            logger.LogInfo(string.Format("{0}: {1}", level, message));
            var request = CreateRequest("/agent/alert_agent", Method.POST);
            request.AddJsonBody(new AgentAlert(message, level, details));

            client.ExecuteAsync(request, response =>
            {
                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Created)
                {
                    logger.LogError(string.Format("Failed to send error to CloudHealth API({0}): {1}", response.StatusCode, response.Content));
                } else {
                    logger.LogError("Successfully logged agent error with CloudHealth API");
                }
            });
        }

        public AsyncRequestHandle Checkin(AgentInfo info, Action<IRestResponse, AgentStatus> callback) 
        {
            logger.LogInfo("Checkin {0} for cloud {1} with key {2}", info.Identifier, info.CloudName, accessToken);
            var request = CreateRequest("/agent/checkin", Method.POST);
            request.AddJsonBody(info);

            var resp = client.ExecuteAsync<AgentStatus>(request, response => {
                callback(response, response.Data);
            });

            return new AsyncRequestHandle(resp);
        }

        public AsyncRequestHandle RegisterAgent(AgentInfo info, Action<IRestResponse, AgentStatus> callback)
        {
            logger.LogInfo("Registering {0} for cloud {1} with key {2}", info.Identifier, info.CloudName, accessToken);
            var request = CreateRequest("/agent/register", Method.POST);
            request.AddJsonBody(info);

            var resp = client.ExecuteAsync<AgentStatus>(request, response =>
            {
                callback(response, response.Data);
            });

            return new AsyncRequestHandle(resp);
        }

        public AsyncRequestHandle UploadPerformanceData(StreamReader jsonReader, Action<IRestResponse> callback)
        {
            var request = CreateRequest("/agent/upload", Method.POST);
            var body = jsonReader.ReadToEnd();
            jsonReader.Close();
            request.AddParameter("application/json", body, ParameterType.RequestBody);
            var resp = client.ExecuteAsync<UploadResponse>(request, response =>
            {
                callback(response);
            });

            return new AsyncRequestHandle(resp);
        }

        protected RestRequest CreateRequest(string path, Method method) {
            var request = new RestRequest(path, method) {RequestFormat = DataFormat.Json};
            request.AddHeader("Access-Token", accessToken);
            request.JsonSerializer = new RestSharpJsonNetSerializer();
            return request;
        }

    }
}
