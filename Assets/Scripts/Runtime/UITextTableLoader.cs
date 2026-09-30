using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads the language table into <see cref="UIText"/> when the page starts.
///
/// The table is a CSV file in StreamingAssets, so in a WebGL build it sits
/// beside the build on the web server
/// (<c>StreamingAssets/Localization/ui_text.csv</c>) and can be edited there,
/// in any text editor or spreadsheet, without rebuilding: reload the page to
/// see the change. Until it loads, and if it cannot be read, the English copy
/// compiled into the views is shown.
///
/// The language comes from the page address (<c>?lang=de</c>), otherwise from
/// <see cref="defaultLanguage"/>. The hosting page can also call, through
/// <c>unityInstance.SendMessage("Digital Twin Runtime", ...)</c>:
/// <c>SetLanguage("de")</c>, <c>LoadLanguageCsv(csvText)</c> and
/// <c>ReloadLanguageTable()</c>.
///
/// Created automatically on the Digital Twin Runtime object when a scene has
/// none, so existing scenes need no setup.
/// </summary>
[DisallowMultipleComponent]
public sealed class UITextTableLoader : MonoBehaviour
{
    public const string RuntimeObjectName = "Digital Twin Runtime";
    public const string DefaultTablePath = "Localization/ui_text.csv";

    [Tooltip("Path of the table under StreamingAssets.")]
    [SerializeField] private string tablePath = DefaultTablePath;

    [Tooltip("Language shown when the page address does not choose one. Also the column " +
             "an empty cell falls back to.")]
    [SerializeField] private string defaultLanguage = UIText.DefaultLanguage;

    [Tooltip("Lets the page address choose the language, as ?lang=de.")]
    [SerializeField] private bool readLanguageFromUrl = true;

    [SerializeField] private string urlParameter = "lang";

    [Tooltip("Gives up on the table after this many seconds and keeps the compiled English copy.")]
    [SerializeField, Min(1f)] private float timeoutSeconds = 10f;

    private Coroutine loading;
    private string requestedLanguage;

    /// <summary>True once a table has been read, from the file or the page.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Why the last load failed, or null.</summary>
    public string LastError { get; private set; }

    public event Action<UITextTableLoader> Loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureLoader()
    {
        if (FindAnyObjectByType<UITextTableLoader>(FindObjectsInactive.Include) != null)
            return;

        // On the runtime object, so SendMessage reaches it under the same
        // name as the telemetry ingestor.
        GameObject host = GameObject.Find(RuntimeObjectName);
        if (host == null)
        {
            host = new GameObject("UI Text Table");
            DontDestroyOnLoad(host);
        }
        host.AddComponent<UITextTableLoader>();
    }

    private void Awake()
    {
        requestedLanguage = defaultLanguage;
        if (readLanguageFromUrl && TryReadUrlLanguage(Application.absoluteURL, urlParameter, out string fromUrl))
            requestedLanguage = fromUrl;
    }

    private void Start()
    {
        ReloadLanguageTable();
    }

    private void OnDisable()
    {
        if (loading != null)
        {
            StopCoroutine(loading);
            loading = null;
        }
    }

    // -----------------------------------------------------------------------
    // Page API (unityInstance.SendMessage)
    // -----------------------------------------------------------------------

    /// <summary>Shows a language the table has a column for.</summary>
    public void SetLanguage(string language)
    {
        requestedLanguage = language;
        if (!UIText.SetLanguage(language))
            Debug.LogWarning($"Language '{language}' is not in the language table; keeping '{UIText.Language}'.", this);
    }

    /// <summary>Replaces the table with CSV text sent by the page.</summary>
    public void LoadLanguageCsv(string csv)
    {
        Apply(csv, "the page");
    }

    /// <summary>Reads the table file again, after it was edited on the server.</summary>
    public void ReloadLanguageTable()
    {
        if (!isActiveAndEnabled)
            return;
        if (loading != null)
            StopCoroutine(loading);
        loading = StartCoroutine(Load());
    }

    // -----------------------------------------------------------------------

    private IEnumerator Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath, tablePath ?? DefaultTablePath).Replace('\\', '/');

        // WebGL (and Android) serve StreamingAssets over the network.
        if (path.Contains("://"))
        {
            using UnityWebRequest request = UnityWebRequest.Get(path);
            request.timeout = Mathf.CeilToInt(timeoutSeconds);
            yield return request.SendWebRequest();
            loading = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail($"could not read '{path}': {request.error}");
                yield break;
            }

            Apply(request.downloadHandler.text, path);
            yield break;
        }

        loading = null;
        if (!File.Exists(path))
        {
            Fail($"no table at '{path}'");
            yield break;
        }

        string csv;
        try
        {
            csv = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            Fail($"could not read '{path}': {exception.Message}");
            yield break;
        }

        Apply(csv, path);
    }

    private void Apply(string csv, string origin)
    {
        if (!UIText.LoadCsv(csv, defaultLanguage, out string error))
        {
            Fail($"rejected the table from {origin}: {error}");
            return;
        }

        LastError = null;
        IsLoaded = true;
        if (!string.IsNullOrWhiteSpace(requestedLanguage) && !UIText.SetLanguage(requestedLanguage))
            Debug.LogWarning($"Language '{requestedLanguage}' is not in the language table; showing '{UIText.Language}'.", this);
        Loaded?.Invoke(this);
    }

    private void Fail(string error)
    {
        LastError = error;
        Debug.LogWarning($"Language table: {error}. Showing the built-in English copy.", this);
        Loaded?.Invoke(this);
    }

    /// <summary>Reads <paramref name="parameter"/> from the query string of <paramref name="url"/>.</summary>
    public static bool TryReadUrlLanguage(string url, string parameter, out string language)
    {
        language = null;
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(parameter))
            return false;

        int query = url.IndexOf('?');
        if (query < 0)
            return false;
        int fragment = url.IndexOf('#', query);
        string text = fragment < 0 ? url.Substring(query + 1) : url.Substring(query + 1, fragment - query - 1);

        foreach (string pair in text.Split('&'))
        {
            int equals = pair.IndexOf('=');
            string name = equals < 0 ? pair : pair.Substring(0, equals);
            if (!string.Equals(Uri.UnescapeDataString(name), parameter, StringComparison.OrdinalIgnoreCase))
                continue;

            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(equals + 1).Replace('+', ' '));
            value = value.Trim();
            // A language code is short and plain; anything else is ignored.
            if (value.Length == 0 || value.Length > 16)
                return false;
            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
            }
            language = value;
            return true;
        }
        return false;
    }
}
