$ErrorActionPreference = 'Stop'
# Run in Windows PowerShell 5.1 so the actual .NET Framework runtime is checked.
$projectRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $PSScriptRoot 'Dlna.check.dll'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:library "/out:$dll" (Join-Path $projectRoot 'src\Dlna.cs')
if ($LASTEXITCODE -ne 0) { throw 'Framework compilation failed.' }
[Reflection.Assembly]::LoadFrom($dll) | Out-Null
$httpSource = @'

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using GoggleCast;
public static class DlnaMockCheck {
 public static string Run() { return RunAsync().GetAwaiter().GetResult(); }
 static async Task<string> RunAsync() {
  var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start();
  int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
  string baseUrl = "http://127.0.0.1:" + port;
  string captured = null;
  var serve = Task.Run(async delegate {
   for (int n=0; n<8; n++) {
    using(var c=await tcp.AcceptTcpClientAsync()) using(var stream=c.GetStream()) {
     var header = new StringBuilder(); int ch;
     while((ch=stream.ReadByte())>=0) {header.Append((char)ch); if(header.ToString().EndsWith("\r\n\r\n"))break;}
     string h=header.ToString(); int len=0;
     foreach(string line in h.Split(new[]{"\r\n"},StringSplitOptions.None)) if(line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len=Int32.Parse(line.Substring(15).Trim());
     byte[] b=new byte[len]; int pos=0; while(pos<len){int read=stream.Read(b,pos,len-pos);if(read==0)throw new Exception("eof");pos+=read;}
     string body=Encoding.UTF8.GetString(b); string response; string status="200 OK";
     if(h.StartsWith("GET /device.xml")) response="<root><URLBase>"+baseUrl+"/base/</URLBase><device><friendlyName>Parent without AVTransport</friendlyName><deviceList><device><friendlyName>Mock Goggles &amp; test</friendlyName><modelName>Mock3</modelName><UDN>uuid:mock</UDN><serviceList><service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>transport</controlURL></service></serviceList></device></deviceList></device></root>";
     else if(h.StartsWith("GET /evil.xml")) response="<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><root>&x;</root>";
     else if(h.StartsWith("GET /slow.xml")) {await Task.Delay(1000); response="<root/>";}
     else {
      if(!h.StartsWith("POST /base/transport"))throw new Exception("wrong control URL");
      var doc=new XmlDocument();doc.LoadXml(body);XmlNode node=doc.SelectSingleNode("//*[local-name()='Body']/*");string action=node.LocalName;
      if(h.IndexOf("SOAPACTION: \"urn:schemas-upnp-org:service:AVTransport:1#"+action+"\"",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("SOAP action missing");
      if(action=="SetAVTransportURI") captured=node.SelectSingleNode("CurrentURIMetaData").InnerText;
      if(action=="Stop") {status="500 Internal Server Error";response="<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><s:Fault><faultstring>UPnPError</faultstring><detail><UPnPError><errorCode>701</errorCode><errorDescription>Transition unavailable</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>";}
      else response="<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><u:"+action+"Response xmlns:u='urn:schemas-upnp-org:service:AVTransport:1'>"+(action=="GetTransportInfo"?"<CurrentTransportState>PLAYING</CurrentTransportState>":"")+"</u:"+action+"Response></s:Body></s:Envelope>";
     }
     byte[] content=Encoding.UTF8.GetBytes(response);byte[] headers=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Type: text/xml; charset=utf-8\r\nContent-Length: "+content.Length+"\r\nConnection: close\r\n\r\n");
     try {await stream.WriteAsync(headers,0,headers.Length);await stream.WriteAsync(content,0,content.Length);}catch(IOException){}
    }
   }
  });
  try {
   DlnaDevice d=await DlnaDiscovery.FromAddressAsync(baseUrl+"/device.xml",CancellationToken.None);
   if(d.Name!="Mock Goggles & test" || d.ControlUri.AbsoluteUri!=baseUrl+"/base/transport")throw new Exception("description parsing failed");
   var client=new DlnaClient(d);await client.SetUriAsync(baseUrl+"/video.mp4?a=1&b=2","\u4e2d\u6587 & <test>","video/mp4",false,CancellationToken.None);
   var metadata=new XmlDocument();metadata.LoadXml(captured);
   if(metadata.SelectSingleNode("//*[local-name()='title']").InnerText!="\u4e2d\u6587 & <test>" || metadata.SelectSingleNode("//*[local-name()='res']").InnerText!=baseUrl+"/video.mp4?a=1&b=2" || !captured.Contains("DLNA.ORG_OP=01"))throw new Exception("DIDL escaping failed");
   await client.SetUriAsync(baseUrl+"/live.ts","live","video/mp2t",true,CancellationToken.None);
   if(!captured.Contains("DLNA.ORG_OP=00"))throw new Exception("live seek mode failed");
   await client.PlayAsync(CancellationToken.None);if(await client.GetTransportStateAsync(CancellationToken.None)!="PLAYING")throw new Exception("state failed");
   bool fault=false;try{await client.StopAsync(CancellationToken.None);}catch(IOException e){fault=e.Message.Contains("701")&&e.Message.Contains("Transition unavailable");}if(!fault)throw new Exception("SOAP diagnostic failed");
   bool dtd=false;try{await DlnaDiscovery.FromAddressAsync(baseUrl+"/evil.xml",CancellationToken.None);}catch(XmlException){dtd=true;}if(!dtd)throw new Exception("DTD accepted");
   bool cancelled=false;using(var cancel=new CancellationTokenSource(100)){try{await DlnaDiscovery.FromAddressAsync(baseUrl+"/slow.xml",cancel.Token);}catch(OperationCanceledException){cancelled=true;}}if(!cancelled)throw new Exception("cancellation failed");
   await serve;return "PASS: nested device, URLBase/controlURL, SOAPAction, DIDL Unicode/escaping, file OP=01/live OP=00, Play/GetTransportInfo, HTTP500 UPnP701, DTD rejection, cancellation";
  } finally {tcp.Stop();}
 }
}
'@
$ssdpSource = @'
using System; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading; using System.Threading.Tasks; using GoggleCast;
public static class SsdpMockCheck {
 public static string Run() {return Work().GetAwaiter().GetResult();}
 static async Task<string> Work() {
  var http = new TcpListener(IPAddress.Loopback,0); http.Start(); int port=((IPEndPoint)http.LocalEndpoint).Port;
  var udp = new UdpClient(AddressFamily.InterNetwork); udp.ExclusiveAddressUse=false; udp.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true); udp.Client.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.2"),1900)); int packets=0;
  var receiver=Task.Run(async delegate {try {while(true){var req=await udp.ReceiveAsync();string request=Encoding.ASCII.GetString(req.Buffer);if(request.Contains("MX:"))throw new Exception("unicast MX unexpected");if(!request.Contains("HOST: 127.0.0.2:1900"))throw new Exception("wrong target");Interlocked.Increment(ref packets);byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nLOCATION: http://127.0.0.1:"+port+"/desc.xml\r\nST: urn:schemas-upnp-org:service:AVTransport:1\r\nUSN: uuid:test\r\n\r\n");await udp.SendAsync(response,response.Length,req.RemoteEndPoint);}}catch(ObjectDisposedException){}catch(SocketException){}});
  var server=Task.Run(async delegate {using(var client=await http.AcceptTcpClientAsync())using(var stream=client.GetStream()){var header=new StringBuilder();int ch;while((ch=stream.ReadByte())>=0){header.Append((char)ch);if(header.ToString().EndsWith("\r\n\r\n"))break;}string xml="<root><device><friendlyName>SSDP mock</friendlyName><serviceList><service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>/control</controlURL></service></serviceList></device></root>";byte[] content=Encoding.UTF8.GetBytes(xml);byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+content.Length+"\r\nConnection: close\r\n\r\n");await stream.WriteAsync(response,0,response.Length);await stream.WriteAsync(content,0,content.Length);}});
  try {var device=await DlnaDiscovery.FromAddressAsync("127.0.0.2",CancellationToken.None);if(device.Name!="SSDP mock"||packets!=6)throw new Exception("discovery failed packets="+packets);await server;return "PASS: directed SSDP, 6 M-SEARCH packets, duplicate LOCATION deduplication, device XML retrieval, total timeout";}finally{udp.Close();http.Stop();receiver.Wait(1000);}
 }
}
'@
$references = @($dll, 'System.Xml.dll', 'System.Core.dll', 'System.dll')
Add-Type -TypeDefinition $httpSource -ReferencedAssemblies $references
Add-Type -TypeDefinition $ssdpSource -ReferencedAssemblies $references
Add-Type -TypeDefinition (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'PlayPreparationMock.cs')) -ReferencedAssemblies $references
$result = @('Runtime: ' + [Environment]::Version, 'Transport targets: loopback HTTP and UDP only; no real receiver control.')
$result += [DlnaMockCheck]::Run()
$result += [SsdpMockCheck]::Run()
$result += [PlayPreparationMock]::Run()
$result | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'protocol-check-results.txt')
$result
