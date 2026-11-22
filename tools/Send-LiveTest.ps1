param(
    [string]$TargetAddress = '127.0.0.1',
    [ValidateRange(1,65535)][int]$Port = 2368,
    [ValidateRange(1,100000)][int]$Packets = 5000
)

# Syntetyczne poprawne pakiety VLP-16: odległość 10 m, 16 wiązek.
# Użycie: ./tools/Send-LiveTest.ps1 -TargetAddress 127.0.0.1 -Port 2368
$client = [System.Net.Sockets.UdpClient]::new()
try {
    for ($packet = 0; $packet -lt $Packets; $packet++) {
        $payload = [byte[]]::new(1206)
        $payload[1204] = 0x37
        $payload[1205] = 0x22
        for ($block = 0; $block -lt 12; $block++) {
            $start = $block * 100
            $payload[$start] = 0xff
            $payload[$start + 1] = 0xee
            $angle = (($packet * 12 + $block) * 120) % 36000
            $payload[$start + 2] = [byte]($angle -band 255)
            $payload[$start + 3] = [byte]($angle -shr 8)
            for ($sample = 0; $sample -lt 32; $sample++) {
                $offset = $start + 4 + $sample * 3
                $payload[$offset] = 0x88
                $payload[$offset + 1] = 0x13
                $payload[$offset + 2] = [byte](80 + ($sample % 16) * 8)
            }
        }
        [void]$client.Send($payload, $payload.Length, $TargetAddress, $Port)
        Start-Sleep -Milliseconds 2
    }
}
finally {
    $client.Dispose()
}
