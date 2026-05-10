$json = $input | ConvertFrom-Json
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Add-Content -Path "_session.log" -Value "-----------"
Add-Content -Path "_session.log" -Value "[$timestamp] Tool: $($json.tool_name)"
Add-Content -Path "_session.log" -Value "Input: $($json.tool_input | ConvertTo-Json -Compress -Depth 5)"
Add-Content -Path "_session.log" -Value "Output: $($json.tool_response | ConvertTo-Json -Compress -Depth 5)"
