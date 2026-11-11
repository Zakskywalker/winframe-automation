public class KeywordFramework
{
    public void ExecuteTestSheet(string csvFilePath)
    {
        SystemLogger.Log($"Starting execution of Keyword Sheet: {csvFilePath}");

        if (!File.Exists(csvFilePath))
        {
            SystemLogger.Log($"Test sheet not found at {csvFilePath}", SystemLogger.LogLevel.ERROR);
            return;
        }

        string[] lines = File.ReadAllLines(csvFilePath);
        
        // Skip header row if present
        for (int i = 1; i < lines.Length; i++) 
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] tokens = lines[i].Split(',');
            if (tokens.Length < 2) continue;

            string action = tokens[0].Trim().ToUpper();
            if (!IntPtr.TryParse(tokens[1].Trim(), out IntPtr targetHwnd))
            {
                SystemLogger.Log($"Invalid HWND provided at row {i + 1}", SystemLogger.LogLevel.ERROR);
                continue;
            }
            
            string value = tokens.Length > 2 ? tokens[2].Trim() : string.Empty;

            ExecuteKeyword(action, targetHwnd, value);
        }
        SystemLogger.Log($"Finished execution of {csvFilePath}");
    }

    private void ExecuteKeyword(string action, IntPtr hwnd, string value)
    {
        SystemLogger.Log($"[KEYWORD] -> ACTION: {action}, HWND: {hwnd}, VALUE: {value}");

        try
        {
            switch (action)
            {
                case "VERIFY":
                    bool result = Win32Automation.VerifyDataValue(hwnd, value);
                    if (!result) throw new Exception("Verification failed.");
                    break;

                case "CLICK":
                    Win32Automation.SendNativeInput(hwnd, "CLICK");
                    break;

                case "TYPE":
                    Win32Automation.SendNativeInput(hwnd, "TYPE", value);
                    break;
                    
                case "HARVEST":
                    Win32Automation.HarvestControls(hwnd);
                    break;

                default:
                    SystemLogger.Log($"Unmapped keyword action: {action}", SystemLogger.LogLevel.WARNING);
                    break;
            }
        }
        catch (Exception ex)
        {
            SystemLogger.Log($"Test Step Failed: {ex.Message}", SystemLogger.LogLevel.ERROR);
        }
    }
}