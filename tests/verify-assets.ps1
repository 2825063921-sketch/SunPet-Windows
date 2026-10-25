$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$config = Get-Content -LiteralPath (Join-Path $root 'pet.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$parsedClips = Get-Content -LiteralPath (Join-Path $root 'Assets\clips.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$clips = @($parsedClips)
if ($clips.Count -ne $config.clipCount) { throw '音频清单数量错误。' }
foreach ($entry in $manifest.files) {
    $path = Join-Path $root $entry.path
    if (!(Test-Path -LiteralPath $path)) { throw ('缺少素材：' + $entry.path) }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) { throw ('素材哈希不匹配：' + $entry.path) }
}
foreach ($clip in $clips) {
    $path = Join-Path $root ('Assets\Playback\' + [IO.Path]::ChangeExtension($clip.file, '.wav'))
    $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($path))
    try {
        $riff = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        $null = $reader.ReadUInt32()
        $wave = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        if ($riff -ne 'RIFF' -or $wave -ne 'WAVE') { throw ('不是WAV：' + $clip.file) }
        $byteRate = 0; $dataSize = 0; $nonzero = $false
        while ($reader.BaseStream.Position + 8 -le $reader.BaseStream.Length) {
            $chunk = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)); $length = $reader.ReadUInt32()
            if ($length -gt ($reader.BaseStream.Length - $reader.BaseStream.Position)) { throw 'WAV块长度错误。' }
            $next = $reader.BaseStream.Position + $length + ($length % 2)
            if ($chunk -eq 'fmt ') {
                if ($length -lt 16 -or $reader.ReadUInt16() -ne 1) { throw '仅支持PCM WAV。' }
                $channels=$reader.ReadUInt16(); $sampleRate=$reader.ReadUInt32(); $byteRate=$reader.ReadUInt32(); $block=$reader.ReadUInt16(); $bits=$reader.ReadUInt16()
                if ($bits -ne 16 -or $channels -lt 1 -or $sampleRate -lt 8000) { throw 'PCM格式错误。' }
            }
            if ($chunk -eq 'data') {
                $dataSize=$length
                for ($sample=0; $sample -lt [Math]::Floor($length / 2); $sample++) { if ([Math]::Abs([int]$reader.ReadInt16()) -gt 32) { $nonzero=$true } }
            }
            $reader.BaseStream.Position=$next
        }
        if ($byteRate -le 0 -or $dataSize -eq 0 -or !$nonzero) { throw ('音频为空或静音：' + $clip.file) }
        if ([Math]::Abs(($dataSize / $byteRate) - [double]$clip.duration) -gt 0.3) { throw ('音频时长不匹配：' + $clip.file) }
    } finally { $reader.Dispose() }
}
Write-Output ('PASS: ' + $manifest.files.Count + ' source hashes; ' + $clips.Count + ' non-silent PCM clips')
