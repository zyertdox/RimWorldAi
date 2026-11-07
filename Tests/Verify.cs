using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SimplePawnDiary;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
Check(DiaryPolicy.InRange(1, 0, 3) && DiaryPolicy.InRange(3, 0, 3), "First entry includes all available event sequences through snapshot");
Check(!DiaryPolicy.InRange(3, 3, 5) && DiaryPolicy.InRange(4, 3, 5), "Next entry excludes the previous boundary");
Check(!DiaryPolicy.InRange(6, 3, 5), "Events arriving during generation are deferred to next entry");
long cursor = 0;
Check(!DiaryPolicy.TryCommit(ref cursor, 0, 4, " ") && cursor == 0, "Empty response cannot advance the period");
Check(DiaryPolicy.TryCommit(ref cursor, 0, 4, "Запись") && cursor == 4, "Successful response advances the cursor to the request snapshot");
Check(!DiaryPolicy.TryCommit(ref cursor, 0, 8, "Устаревший ответ") && cursor == 4, "Stale response cannot overwrite an already committed period");
Check(!DiaryPolicy.TryCommit(ref cursor, 4, 4, "Повтор") && cursor == 4, "Duplicate response cannot consume the same period twice");
Check(DiaryPolicy.InRange(5, cursor, 6), "Future events remain available after successful commit");
var lines = new[] { new EventLine { Sequence=1,Tick=60000,Source="социальный журнал",Text="Чаз поговорила с Виктором." }, new EventLine { Sequence=2,Tick=62500,Source="событие колонии",Text="Прибыл торговый корабль." } };
string source = DiaryPolicy.BuildSourceText(lines);
Check(source.Contains("день 2, 00:00") && source.Contains(lines[0].Text) && source.Contains(lines[1].Text), "Source text retains native facts and chronology labels");
string user = DiaryPolicy.BuildUserPrompt("Чаз", "женский", 65000, lines);
Check(user.Contains("Персонаж: Чаз") && user.Contains("женский") && user.Contains("день 2, 02:00"), "Prompt identifies the selected pawn and request time");
string requestJson = ApiClient.SerializeRequest("gpt-6-luna", DiaryPolicy.SystemPrompt, user);
using (JsonDocument parsed = JsonDocument.Parse(requestJson))
{
    var root = parsed.RootElement;
    Check(root.GetProperty("model").GetString()=="gpt-6-luna" && !root.GetProperty("stream").GetBoolean(), "Wire request uses configured model and nonstreaming mode");
    var messages=root.GetProperty("messages");
    Check(messages.GetArrayLength()==2 && messages[0].GetProperty("role").GetString()=="system" && messages[1].GetProperty("content").GetString()==user, "UTF8 request preserves Russian source text and message roles");
}
Check(ApiClient.ParseResponse("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"  Русский дневник.  \"}}]}")=="Русский дневник.", "Standard Chat Completions response is parsed without dialogue JSONL");
foreach(string invalid in new[]{"not json","{}","{\"choices\":[{\"message\":{\"content\":\" \"}}]}","{\"error\":{\"message\":\"Ошибка модели\"}}"})
{
    bool rejected=false;try{ApiClient.ParseResponse(invalid);}catch(InvalidOperationException){rejected=true;}
    Check(rejected,"Invalid or unsuccessful response rejected: "+invalid.Substring(0,Math.Min(invalid.Length,30)));
}

async Task<(string error,string body,string received)> MockRequest(int status,string response,bool cancel=false)
{
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
    using var serverTimeout=new CancellationTokenSource(5000);
    int port=((IPEndPoint)listener.LocalEndpoint).Port;
    string received="";
    var server=Task.Run(async()=>{
        using var client=await listener.AcceptTcpClientAsync(serverTimeout.Token);using var stream=client.GetStream();
        var header=new List<byte>();int value;
        while((value=stream.ReadByte())!=-1){header.Add((byte)value);if(header.Count>=4&&Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n"))break;}
        string head=Encoding.ASCII.GetString(header.ToArray());
        int length=int.Parse(head.Split("\r\n").Single(x=>x.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
        byte[] input=new byte[length];int offset=0;while(offset<length){int read=await stream.ReadAsync(input.AsMemory(offset,length-offset));if(read==0)break;offset+=read;}
        received=Encoding.UTF8.GetString(input);
        if(cancel){await Task.Delay(500);return;}
        byte[] bytes=Encoding.UTF8.GetBytes(response);
        byte[] headers=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);await stream.WriteAsync(bytes);
    });
    using var cancellation=new CancellationTokenSource();
    cancellation.CancelAfter(cancel?150:5000);
    string body=null,error=null;
    try{body=await Task.Run(()=>ApiClient.Generate($"http://127.0.0.1:{port}/v1/chat/completions","test-model","",user,cancellation.Token));}
    catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
    try{await server;}catch(OperationCanceledException){if(error==null)throw;}finally{listener.Stop();}
    return(error,body,received);
}
var success=await MockRequest(200,"{\"choices\":[{\"message\":{\"content\":\"Сегодня меня вылечили.\"}}]}");
Check(success.error==null&&success.body=="Сегодня меня вылечили.","Real HTTP transport receives and decodes Russian diary text: "+success.error);
using(var parsed=JsonDocument.Parse(success.received))Check(parsed.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()==user,"Real HTTP payload contains selected native events unchanged");
var failure=await MockRequest(500,"{\"error\":{\"message\":\"Test unavailable\"}}");
Check(failure.body==null&&failure.error.Contains("HTTP 500"),"HTTP server failure is surfaced without producing an entry");
var canceled=await MockRequest(200,"",true);
Check(canceled.body==null&&canceled.error.StartsWith("OperationCanceledException"),"Cancellation interrupts an in-flight HTTP request");

using var gamePe=new PEReader(File.OpenRead(args[0]));var md=gamePe.GetMetadataReader();
foreach(var target in new[]{("Verse.PlayLog","Add"),("Verse.BattleLog","Add"),("RimWorld.Archive","Add"),("Verse.Profile.MemoryUtility","ClearAllMapsAndWorld")})
{
    var type=md.TypeDefinitions.Select(md.GetTypeDefinition).Single(t=>md.GetString(t.Namespace)+"."+md.GetString(t.Name)==target.Item1);
    Check(type.GetMethods().Select(md.GetMethodDefinition).Any(m=>md.GetString(m.Name)==target.Item2),"Installed game Harmony target exists: "+target);
}
using var modPe=new PEReader(File.OpenRead(args[1]));var modMd=modPe.GetMetadataReader();
var dependencies=modMd.AssemblyReferences.Select(modMd.GetAssemblyReference).Select(a=>modMd.GetString(a.Name)).ToArray();
Check(!dependencies.Any(d=>d.Contains("RimTalk")||d.Contains("Rimtalk")||d.Contains("Newtonsoft")),"Compiled mod has no RimTalk, Diary, Expand Memory, or external JSON dependency");
Console.WriteLine(checks+" checks passed.");
