# Simple CORS proxy for archive.org PDF files
# Usage: Run alongside dotnet run
param($Port = 52315)

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://+:$Port/")
$listener.Start()
Write-Host "CORS Proxy running on http://localhost:$Port/"

while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $req = $ctx.Request
    $res = $ctx.Response

    # CORS preflight
    if ($req.HttpMethod -eq "OPTIONS") {
        $res.Headers.Add("Access-Control-Allow-Origin", "*")
        $res.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS")
        $res.Headers.Add("Access-Control-Allow-Headers", "*")
        $res.StatusCode = 204
        $res.Close()
        continue
    }

    $url = $req.QueryString["url"]
    if (-not $url) {
        $res.StatusCode = 400
        $res.Close()
        continue
    }

    try {
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")
        $data = $wc.DownloadData($url)
        $res.ContentType = "application/pdf"
        $res.Headers.Add("Access-Control-Allow-Origin", "*")
        $res.ContentLength64 = $data.Length
        $res.OutputStream.Write($data, 0, $data.Length)
    } catch {
        $res.StatusCode = 500
        $res.ContentType = "text/plain"
        $msg = [System.Text.Encoding]::UTF8.GetBytes("Proxy error: $_")
        $res.OutputStream.Write($msg, 0, $msg.Length)
    }
    $res.Close()
}
