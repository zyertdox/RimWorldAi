using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace SimplePawnDiary
{
    [DataContract] public sealed class WireMessage
    {
        [DataMember] public string role;
        [DataMember] public string content;
    }
    [DataContract] public sealed class WireRequest
    {
        [DataMember] public string model;
        [DataMember] public WireMessage[] messages;
        [DataMember] public bool stream;
        [DataMember] public int max_tokens = 4096;
    }
    [DataContract] public sealed class WireChoice { [DataMember] public WireMessage message; }
    [DataContract] public sealed class WireError { [DataMember] public string message; }
    [DataContract] public sealed class WireResponse
    {
        [DataMember] public WireChoice[] choices;
        [DataMember] public WireError error;
    }

    public static class ApiClient
    {
        public static string SerializeRequest(string model, string system, string user)
        {
            var payload = new WireRequest { model = model, stream = false, messages = new[] {
                new WireMessage { role = "system", content = system }, new WireMessage { role = "user", content = user } } };
            using (var memory = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WireRequest)).WriteObject(memory, payload);
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }

        public static string ParseResponse(string json)
        {
            WireResponse response;
            try
            {
                using (var memory = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    response = (WireResponse)new DataContractJsonSerializer(typeof(WireResponse)).ReadObject(memory);
            }
            catch (Exception) { throw new InvalidOperationException("Сервер вернул некорректный JSON."); }
            if (!string.IsNullOrWhiteSpace(response == null || response.error == null ? null : response.error.message))
                throw new InvalidOperationException(response.error.message);
            string body = response == null || response.choices == null || response.choices.Length == 0 || response.choices[0].message == null
                ? null : response.choices[0].message.content;
            if (string.IsNullOrWhiteSpace(body)) throw new InvalidOperationException("Сервер вернул пустую запись. Период не изменён.");
            return body.Trim();
        }

        public static string Generate(string endpoint, string model, string apiKey, string user, CancellationToken cancellation)
        {
            Uri address;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out address) || (address.Scheme != "http" && address.Scheme != "https"))
                throw new InvalidOperationException("Укажите полный HTTP-адрес Chat Completions в настройках «Простой дневник».");
            if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("В настройках не указана модель.");
            cancellation.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = 300000;
            request.ReadWriteTimeout = 300000;
            if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey.Trim();
            byte[] payload = Encoding.UTF8.GetBytes(SerializeRequest(model.Trim(), DiaryPolicy.SystemPrompt, user));
            request.ContentLength = payload.Length;
            using (cancellation.Register(request.Abort))
            {
                try
                {
                    using (Stream output = request.GetRequestStream()) output.Write(payload, 0, payload.Length);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        return ParseResponse(reader.ReadToEnd());
                }
                catch (WebException ex)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var response = ex.Response as HttpWebResponse;
                    if (response == null) throw new InvalidOperationException("Не удалось связаться с сервером: " + ex.Message);
                    using (response)
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        string detail = reader.ReadToEnd();
                        if (detail.Length > 600) detail = detail.Substring(0, 600);
                        throw new InvalidOperationException("Сервер вернул HTTP " + (int)response.StatusCode + ": " + detail);
                    }
                }
            }
        }
    }
}
