using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

public class FirebaseSymbolUploader : EditorWindow
{
    // --- JSON Data Structures ---
    [System.Serializable]
    public class FirebaseProjectInfo
    {
        public string projectId;
        public string displayName;
    }

    [System.Serializable]
    public class FirebaseProjectList
    {
        public List<FirebaseProjectInfo> projects;
    }

    [System.Serializable]
    public class FirebaseAppInfo
    {
        public string appId;
        public string displayName;
        public string projectId;
        public string platform;
    }

    [System.Serializable]
    public class FirebaseAppList
    {
        public List<FirebaseAppInfo> apps;
    }

    // --- State Variables ---
    private string cliPath = "firebase"; 
    private string symbolPath = "";
    private bool isProcessing = false;
    private Vector2 scrollPosition;
    private string outputLog = "";
    private bool needsLogin = false;

    // Project Selection
    private List<FirebaseProjectInfo> fetchedProjects = new List<FirebaseProjectInfo>();
    private string[] projectDropdownOptions = new string[] { "No projects loaded. Click 'Fetch Projects'" };
    private int selectedProjectIndex = 0;
    private string selectedProjectId = "";

    // App Selection
    private List<FirebaseAppInfo> fetchedApps = new List<FirebaseAppInfo>();
    private string[] appDropdownOptions = new string[] { "Waiting for project selection..." };
    private int selectedAppIndex = 0;
    private string selectedAppId = "";

    [MenuItem("Dev Menu/Tools/Firebase Crashlytics Symbol Uploader")]
    public static void ShowWindow()
    {
        GetWindow<FirebaseSymbolUploader>("Symbol Uploader", true);
    }

    void OnGUI()
    {
        GUILayout.Label("Firebase Symbol Uploader", EditorStyles.boldLabel);
        cliPath = EditorGUILayout.TextField("Firebase CLI Path", cliPath);

        GUILayout.Space(10);
        
        // --- AUTHENTICATION WARNING ---
        if (needsLogin)
        {
            EditorGUILayout.HelpBox("Firebase CLI requires authentication. Please log in via the external terminal.", MessageType.Warning);
            if (GUILayout.Button("Launch Firebase Login Terminal", GUILayout.Height(25)))
            {
                LaunchLoginTerminal();
            }
            GUILayout.Space(10);
        }

        // --- STEP 1: PROJECT SELECTION ---
        GUILayout.Label("Step 1: Select Project", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        GUI.enabled = !isProcessing;
        
        int prevProjectIndex = selectedProjectIndex;
        selectedProjectIndex = EditorGUILayout.Popup("Firebase Project", selectedProjectIndex, projectDropdownOptions);
        
        if (GUILayout.Button("Fetch Projects", GUILayout.Width(120)))
        {
            FetchFirebaseProjects();
        }
        GUILayout.EndHorizontal();

        // Update selected project ID and reset apps if project changes
        if (fetchedProjects != null && fetchedProjects.Count > 0 && selectedProjectIndex < fetchedProjects.Count)
        {
            selectedProjectId = fetchedProjects[selectedProjectIndex].projectId;
            if (prevProjectIndex != selectedProjectIndex)
            {
                ClearApps();
            }
        }
        GUI.enabled = true;

        GUILayout.Space(10);

        // --- STEP 2: APP SELECTION ---
        GUILayout.Label("Step 2: Select Android App", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        GUI.enabled = !isProcessing && !string.IsNullOrEmpty(selectedProjectId);
        
        selectedAppIndex = EditorGUILayout.Popup("Firebase App", selectedAppIndex, appDropdownOptions);
        
        if (GUILayout.Button("Fetch Apps", GUILayout.Width(120)))
        {
            FetchFirebaseApps();
        }
        GUILayout.EndHorizontal();

        if (fetchedApps != null && fetchedApps.Count > 0 && selectedAppIndex < fetchedApps.Count)
        {
            selectedAppId = fetchedApps[selectedAppIndex].appId;
            EditorGUILayout.LabelField("Selected App ID:", selectedAppId, EditorStyles.miniLabel);
        }
        GUI.enabled = true;

        GUILayout.Space(15);

        // --- STEP 3: SYMBOL LOCATION ---
        GUILayout.Label("Step 3: Select Symbol Location (Directory or .zip)", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        symbolPath = EditorGUILayout.TextField(symbolPath);
        if (GUILayout.Button("Browse Folder", GUILayout.Width(100)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Symbol Directory", "", "");
            if (!string.IsNullOrEmpty(path)) symbolPath = path;
        }
        if (GUILayout.Button("Browse Zip", GUILayout.Width(100)))
        {
            string path = EditorUtility.OpenFilePanel("Select Symbol Zip", "", "zip");
            if (!string.IsNullOrEmpty(path)) symbolPath = path;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(20);

        // --- STEP 4: UPLOAD ---
        GUI.enabled = !isProcessing && !string.IsNullOrEmpty(selectedAppId) && !string.IsNullOrEmpty(symbolPath);
        if (GUILayout.Button(isProcessing ? "Processing..." : "Upload Symbols to Selected App", GUILayout.Height(35)))
        {
            PerformUpload();
        }
        GUI.enabled = true;

        GUILayout.Space(15);
        
        // --- CLI LOG OUTPUT ---
        GUILayout.Label("CLI Output Log:", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, EditorStyles.helpBox, GUILayout.Height(150));
        EditorGUILayout.TextArea(outputLog, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
        
        if (GUILayout.Button("Clear Log", GUILayout.Width(80)))
        {
            outputLog = "";
        }
    }

    // --- CORE LOGIC ---

    private void ClearApps()
    {
        fetchedApps.Clear();
        appDropdownOptions = new string[] { "Click 'Fetch Apps' for this project..." };
        selectedAppIndex = 0;
        selectedAppId = "";
    }

    private async void FetchFirebaseProjects()
    {
        isProcessing = true;
        needsLogin = false;
        AppendLog("Fetching Firebase projects...");

        try
        {
            string rawOutput = await RunFirebaseCommandBlocking("projects:list --json");
            string cleanJsonArray = ExtractJsonArray(rawOutput);
            
            if (!string.IsNullOrEmpty(cleanJsonArray))
            {
                FirebaseProjectList list = JsonUtility.FromJson<FirebaseProjectList>("{\"projects\":" + cleanJsonArray + "}");
                if (list != null && list.projects != null && list.projects.Count > 0)
                {
                    fetchedProjects = list.projects;
                    List<string> options = new List<string>();
                    foreach (var proj in fetchedProjects)
                    {
                        options.Add($"{proj.displayName} ({proj.projectId})");
                    }
                    projectDropdownOptions = options.ToArray();
                    selectedProjectIndex = 0;
                    ClearApps();
                    AppendLog($"Successfully loaded {fetchedProjects.Count} projects.");
                }
                else
                {
                    AppendLog("No projects found.");
                }
            }
        }
        catch (System.Exception e)
        {
            AppendLog($"Error fetching projects: {e.Message}");
        }
        finally
        {
            isProcessing = false;
            Repaint();
        }
    }

    private async void FetchFirebaseApps()
    {
        isProcessing = true;
        AppendLog($"Fetching Android apps for project: {selectedProjectId}...");

        try
        {
            string rawOutput = await RunFirebaseCommandBlocking($"apps:list ANDROID --project {selectedProjectId} --json");
            string cleanJsonArray = ExtractJsonArray(rawOutput);
            
            if (!string.IsNullOrEmpty(cleanJsonArray))
            {
                FirebaseAppList list = JsonUtility.FromJson<FirebaseAppList>("{\"apps\":" + cleanJsonArray + "}");
                if (list != null && list.apps != null && list.apps.Count > 0)
                {
                    fetchedApps = list.apps;
                    List<string> options = new List<string>();
                    foreach (var app in fetchedApps)
                    {
                        // Some apps might not have a display name, fallback to App ID
                        string name = string.IsNullOrEmpty(app.displayName) ? "Unnamed App" : app.displayName;
                        options.Add($"{name} ({app.appId})");
                    }
                    appDropdownOptions = options.ToArray();
                    selectedAppIndex = 0;
                    AppendLog($"Successfully loaded {fetchedApps.Count} Android apps.");
                }
                else
                {
                    AppendLog("No Android apps found in this project.");
                    ClearApps();
                }
            }
        }
        catch (System.Exception e)
        {
            AppendLog($"Error fetching apps: {e.Message}");
        }
        finally
        {
            isProcessing = false;
            Repaint();
        }
    }

    private async void PerformUpload()
    {
        isProcessing = true;
        outputLog += "----------------------------------------\n";
        AppendLog($"Initiating upload sequence for App ID: {selectedAppId}...\n");
        
        try
        {
            await RunFirebaseCommandStreaming($"crashlytics:symbols:upload --app={selectedAppId} \"{symbolPath}\"");
        }
        catch (System.Exception e)
        {
            AppendLog($"\nEXCEPTION: {e.Message}");
        }
        finally
        {
            isProcessing = false;
            Repaint();
        }
    }

    // --- UTILITIES ---

    // Safely extracts the array string [...] from the CLI output string
    private string ExtractJsonArray(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return null;

        int startIndex = rawJson.IndexOf('[');
        int endIndex = rawJson.LastIndexOf(']');

        if (startIndex == -1 || endIndex == -1)
        {
            AppendLog("Failed to locate JSON payload array in CLI output.");
            return null;
        }

        return rawJson.Substring(startIndex, endIndex - startIndex + 1);
    }

    private void LaunchLoginTerminal()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.UseShellExecute = true;

        #if UNITY_EDITOR_WIN
        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = $"/c \"{cliPath}\" login & echo. & echo You can close this window now. & pause";
        #else
        startInfo.FileName = "/usr/bin/osascript";
        startInfo.Arguments = $"-e 'tell application \"Terminal\" to do script \"{cliPath} login\"'";
        #endif

        Process.Start(startInfo);
        AppendLog("External login configuration terminal spawned.");
    }

    // Runs a command silently and waits for the full string output (used for JSON fetching)
    private Task<string> RunFirebaseCommandBlocking(string arguments)
    {
        var tcs = new TaskCompletionSource<string>();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        
        #if UNITY_EDITOR_WIN
        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = $"/c \"\"{cliPath}\" {arguments}\"";
        #else
        startInfo.FileName = "/bin/bash";
        startInfo.Arguments = $"-c \"'{cliPath}' {arguments}\"";
        #endif

        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        Process process = new Process { StartInfo = startInfo };
        
        string stdOut = "";
        string stdErr = "";

        process.OutputDataReceived += (s, e) => { if (e.Data != null) stdOut += e.Data + "\n"; };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) stdErr += e.Data + "\n"; };

        process.EnableRaisingEvents = true;
        process.Exited += (s, e) =>
        {
            EditorApplication.delayCall += () =>
            {
                if (process.ExitCode != 0)
                {
                    if (stdErr.Contains("authentication") || stdErr.Contains("login") || stdOut.Contains("login"))
                    {
                        needsLogin = true;
                    }
                    AppendLog($"Command Failed: {stdErr}");
                    tcs.SetResult(string.Empty);
                }
                else
                {
                    tcs.SetResult(stdOut);
                }
                process.Dispose();
            };
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return tcs.Task;
    }

    // Runs a command and streams output live to the log (used for long upload operations)
    private Task RunFirebaseCommandStreaming(string arguments)
    {
        var tcs = new TaskCompletionSource<bool>();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        
        #if UNITY_EDITOR_WIN
        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = $"/c \"\"{cliPath}\" {arguments}\"";
        #else
        startInfo.FileName = "/bin/bash";
        startInfo.Arguments = $"-c \"'{cliPath}' {arguments}\"";
        #endif

        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        Process process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendLog(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendLog($"ERROR: {e.Data}"); };

        process.Exited += (s, e) =>
        {
            EditorApplication.delayCall += () =>
            {
                if (process.ExitCode == 0)
                    AppendLog("\nSUCCESS: Symbol upload sequence executed successfully!");
                else
                    AppendLog($"\nFAILED: Process tracking dropped out with exit code {process.ExitCode}.");
                
                process.Dispose();
                tcs.SetResult(true);
            };
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return tcs.Task;
    }

    private void AppendLog(string message)
    {
        UnityEngine.Debug.Log($"[Firebase CLI] {message}");
        EditorApplication.delayCall += () => 
        {
            outputLog += message + "\n";
            scrollPosition.y = float.MaxValue; 
            Repaint();
        };
    }
}