using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlockchainObserver.Utils
{
    public class JsonRpcClient
    {
        private int _id;
        public string UserName { get; set; }
        public string Password { get; set; }
        public string rpcVersion { get; set; }
        public string Url { get; set; }

        public JsonRpcClient(string rpcV = "2.0")
        {
            this.rpcVersion = rpcV;
        }

        public virtual object Invoke(string method, params object[] args)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(this.Url);

            if (!string.IsNullOrEmpty(UserName))
                request.Credentials = new NetworkCredential(UserName, Password);

            request.Method = "POST";
            request.ContentType = "application/json";

            JObject call = new JObject();
            call["jsonrpc"] = rpcVersion;
            call["id"] = ++_id;
            call["method"] = method;
            if (args != null && args.Length > 0)
            {
                call["params"] = args[0] is JObject && args.Length == 1 ? (JToken)args[0] : JArray.FromObject(args);
            }
            else
            {
                call["params"] = new JArray();
            }
            string jsonString = call.ToString();

            byte[] postJsonBytes = Encoding.UTF8.GetBytes(jsonString);
            request.ContentLength = postJsonBytes.Length;
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(postJsonBytes, 0, postJsonBytes.Length);
            }

            try
            {
                using (WebResponse response = request.GetResponse())
                using (Stream stream2 = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream2, Encoding.UTF8))
                {
                    JObject answer = new JObject();
                    try
                    {
                        string data = reader.ReadToEnd();
                        answer = JObject.Parse(data);
                    }
                    catch (Exception e)
                    {
                        throw new Exception("JsonRpcClient Error: " + answer.ToString() + "; Status Code: " + ((HttpWebResponse)response).StatusCode, e.InnerException);
                    }

                    if (answer["error"] != null && answer["error"].HasValues)
                        OnError(answer["error"], jsonString);

                    return answer["result"];
                }
            }
            catch (WebException e)
            {
                if (e.Response != null)
                {
                    using (Stream stream = e.Response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        throw new Exception("JsonRpcClient Error: " + reader.ReadToEnd() + "; Status Code: " + ((HttpWebResponse)e.Response).StatusCode +
                            "; Request: " + jsonString, e.InnerException);
                    }
                }
                else
                {
                    throw new WebException("JsonRpcClient Error: ", e.InnerException);
                }
            }
        }

        /// <param name="request">What query caused the error</param>
        protected virtual void OnError(object errorObject, object request)
        {
            JObject error = errorObject as JObject;

            if (error != null)
                throw new Exception(error["message"].ToString() + "; Request: " + request as string);

            throw new Exception(errorObject as string);
        }
    }
}
