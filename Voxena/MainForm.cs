using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Voxena.Infrastructure;
using Voxena.Models;
using Voxena.Services;

namespace Voxena
{
    public sealed class MainForm : Form
    {
        private readonly WebView2 _web=new WebView2();
        private readonly JavaScriptSerializer _json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=100};
        private readonly SettingsStore _settingsStore=new SettingsStore();
        private readonly DownloadService _downloads=new DownloadService();
        private readonly HardwareInfo _hardware;
        private readonly RuntimeBootstrapService _runtime;
        private readonly ModelManager _models;
        private readonly VoiceLibraryService _voices;
        private readonly RussianStressService _stress;
        private readonly AudioPostProcessor _post;
        private readonly TtsEngine _tts;
        private AppSettings _settings;
        private CancellationTokenSource _operationCts;
        private readonly List<GenerationResult> _lastGenerations=new List<GenerationResult>();
        private readonly object _engineLineGate=new object();
        private readonly System.Windows.Forms.Timer _engineLineTimer=new System.Windows.Forms.Timer();
        private string _pendingEngineLine;
        private bool _webReady,_closing,_voiceDialogOpen;

        public MainForm()
        {
            AppPaths.EnsureAll();AppPaths.ClearGeneratedCache();AppPaths.CleanupLegacyPreviewCopies();_settings=_settingsStore.Load();_hardware=HardwareDetector.Detect();_runtime=new RuntimeBootstrapService(_downloads);_models=new ModelManager(_runtime);_voices=new VoiceLibraryService(_models,_runtime);_stress=new RussianStressService(_runtime);_post=new AudioPostProcessor(_runtime);_tts=new TtsEngine(_models,_voices,_runtime,_stress,_post);
            Text="Voxena";Icon=LoadIconSafe();StartPosition=FormStartPosition.CenterScreen;MinimumSize=new Size(1000,700);Size=new Size(1360,880);BackColor=Color.FromArgb(8,12,24);FormBorderStyle=FormBorderStyle.None;KeyPreview=true;
            _web.Dock=DockStyle.Fill;_web.Margin=Padding.Empty;Controls.Add(_web);
            _engineLineTimer.Interval=125;_engineLineTimer.Tick+=(s,e)=>FlushEngineLine();_engineLineTimer.Start();
            Load+=async(s,e)=>await InitializeWebAsync();Shown+=(s,e)=>ApplyNativeTheme();Resize+=(s,e)=>SendWindowState();FormClosing+=OnFormClosing;
        }
        protected override void WndProc(ref Message m){if(m.Msg==NativeMethods.WM_NCHITTEST&&WindowState==FormWindowState.Normal){base.WndProc(ref m);if((int)m.Result==1||(int)m.Result==0){int hit=NativeMethods.HitTestResize(this,m.LParam,8);if(hit!=0)m.Result=(IntPtr)hit;}return;}base.WndProc(ref m);}
        private async Task InitializeWebAsync()
        {
            try{var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(AppPaths.Cache,"WebView2"));await _web.EnsureCoreWebView2Async(env);_web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.voxena",AppPaths.Web,CoreWebView2HostResourceAccessKind.Allow);_web.CoreWebView2.SetVirtualHostNameToFolderMapping("audio.voxena",AppPaths.Generated,CoreWebView2HostResourceAccessKind.Allow);_web.CoreWebView2.SetVirtualHostNameToFolderMapping("voices.voxena",AppPaths.Voices,CoreWebView2HostResourceAccessKind.Allow);_web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;_web.CoreWebView2.Settings.AreDevToolsEnabled=_settings.EnableDevTools;_web.CoreWebView2.Settings.IsStatusBarEnabled=false;_web.CoreWebView2.Settings.IsZoomControlEnabled=false;_web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled=_settings.EnableDevTools;_web.CoreWebView2.WebMessageReceived+=CoreWebView2_WebMessageReceived;_web.CoreWebView2.NavigationStarting+=CoreWebView2_NavigationStarting;_web.CoreWebView2.NewWindowRequested+=CoreWebView2_NewWindowRequested;_web.Source=new Uri("https://app.voxena/index.html");ApplyNativeTheme();}
            catch(Exception ex){Logger.Write("WebView2 init failed: "+ex);MessageBox.Show("Voxena could not initialize Microsoft Edge WebView2.\n\n"+ex.Message,"Voxena",MessageBoxButtons.OK,MessageBoxIcon.Error);Close();}
        }
        private void CoreWebView2_NavigationStarting(object sender,CoreWebView2NavigationStartingEventArgs e){Uri u;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out u))return;if(u.Host.Equals("app.voxena",StringComparison.OrdinalIgnoreCase))return;e.Cancel=true;OpenExternal(e.Uri);}
        private void CoreWebView2_NewWindowRequested(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;OpenExternal(e.Uri);}
        private void CoreWebView2_WebMessageReceived(object sender,CoreWebView2WebMessageReceivedEventArgs e)
        {
            BridgeRequest req=null;
            try{req=_json.Deserialize<BridgeRequest>(e.WebMessageAsJson);}
            catch(Exception ex){Logger.Write("Bridge parse error: "+ex);}
            if(req==null||string.IsNullOrWhiteSpace(req.Action))return;

            // Do not keep the WebView2 COM callback alive while a model installs for minutes.
            // Every bridge task owns its exception boundary, so background failures cannot
            // escape an async-void event and terminate WinForms without a visible error.
            RunBridgeRequestSafeAsync(req);
        }
        private async void RunBridgeRequestSafeAsync(BridgeRequest req)
        {
            try
            {
                switch(req.Action)
                {
                    case "appReady":_webReady=true;SendState();SendWindowState();break;
                    case "titlebar":HandleTitlebar(GetString(req.Payload,"command"));break;
                    case "saveSettings":SaveSettingsFromPayload(req.Payload);SendState();break;
                    case "installModels":await InstallModelsAsync(GetStringArray(req.Payload,"ids"),true);break;
                    case "skipFirstSetup":SkipFirstSetup();break;
                    case "installModel":await InstallModelsAsync(new[]{GetString(req.Payload,"id")},false);break;
                    case "removeModel":RemoveModel(GetString(req.Payload,"id"));break;
                    case "cancelOperation":if(_operationCts!=null)_operationCts.Cancel();break;
                    case "browseVoice":QueueBrowseVoice(req.Payload);break;
                    case "importAudioData":await ImportAudioDataAsync(req.Payload);break;
                    case "deleteVoice":DeleteVoice(GetString(req.Payload,"id"));break;
                    case "previewVoice":await PreviewVoiceAsync(GetString(req.Payload,"id"));break;
                    case "generate":await GenerateAsync(req.Payload);break;
                    case "pickOutputFolder":PickOutputFolder();break;
                    case "openOutput":OpenFolder(_settings.OutputFolder);break;
                    case "saveGeneratedAs":SaveGeneratedAs(GetInt(req.Payload,"index",0));break;
                    case "revealGenerated":RevealGenerated(GetInt(req.Payload,"index",0));break;
                    case "openExternal":OpenExternal(GetString(req.Payload,"url"));break;
                }
            }
            catch(OperationCanceledException)
            {
                // Operations that own a cancellation UI handle it themselves. This guard is
                // for bridge actions that were cancelled before reaching their inner handler.
                Logger.Write("Bridge action cancelled: "+req.Action);
            }
            catch(Exception ex)
            {
                Logger.Write("Bridge action failed ("+req.Action+"): "+ex);
                PostError(ex);
            }
        }
        private async Task InstallModelsAsync(IEnumerable<string> ids,bool finishFirstRun)
        {
            var list=(ids??Enumerable.Empty<string>()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(list.Count==0)throw new InvalidOperationException("Select at least one model.");
            if(!TryStartOperation("Preparing selected models…"))return;
            try
            {
                Logger.Write("First/setup install started: "+string.Join(", ",list));
                var progress=CreateProgress();Action<string> line=CreateEngineLineSink();
                await _models.InstallManyAsync(list,progress,_operationCts.Token,line);
                Logger.Write("Model install completed: "+string.Join(", ",list));
                Toast("success",list.Count==1?Local("Model is ready.","Модель готова.","Модель готова."):Local("Selected models are ready.","Выбранные модели готовы.","Вибрані моделі готові."));
            }
            catch(OperationCanceledException){Logger.Write("Model install cancelled.");Toast("info",Local("Operation cancelled. Downloads can resume later.","Операция отменена. Загрузку можно продолжить позже.","Операцію скасовано. Завантаження можна продовжити пізніше."));}
            catch(Exception ex)
            {
                Logger.Write("Model install UI failure: "+ex);
                PostError(Local("Model installation failed.","Не удалось установить модель.","Не вдалося встановити модель."),ex.ToString());
            }
            finally
            {
                // A failed item must not hide models that finished successfully. On first
                // setup, allow the user into the app as soon as at least one selection is ready.
                if(finishFirstRun&&list.Any(_models.IsInstalled))
                {
                    _settings.FirstRunCompleted=true;
                    _settingsStore.Save(_settings);
                }
                SendState();
                EndOperation();
            }
        }
        private void SkipFirstSetup()
        {
            _settings.FirstRunCompleted=true;
            _settingsStore.Save(_settings);
            SendState();
            Toast("info",Local("You can install models later from Model manager.","Модели можно установить позже через менеджер моделей.","Моделі можна встановити пізніше через менеджер моделей."));
        }
        private void DeleteVoice(string id)
        {
            var voice=_voices.Find(id);
            if(voice==null||!string.Equals(voice.Kind,"custom",StringComparison.OrdinalIgnoreCase))return;
            string prompt=Local(
                "Delete the cloned voice ‘"+voice.Name+"’ ("+voice.ModelName+")? Its cached conditioning and reference files will be removed from this PC.",
                "Удалить клонированный голос «"+voice.Name+"» ("+voice.ModelName+")? Его кэш и файлы референса будут удалены с этого ПК.",
                "Видалити клонований голос «"+voice.Name+"» ("+voice.ModelName+")? Його кеш і файли референсу буде видалено з цього ПК.");
            if(MessageBox.Show(this,prompt,"Voxena",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            _voices.Delete(id);
            if(string.Equals(_settings.SelectedVoiceId,id,StringComparison.OrdinalIgnoreCase))
            {
                _settings.SelectedVoiceId="";
                _settingsStore.Save(_settings);
            }
            SendState();
            Toast("success",Local("Voice removed.","Голос удалён.","Голос видалено."));
        }
        private void RemoveModel(string id){var p=ModelCatalog.Get(id);if(p==null)return;if(MessageBox.Show(this,"Remove "+p.Name+" model files and its isolated runtime? Cloned voice metadata is kept and can be reused after reinstalling the model.","Voxena",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;_models.Remove(id);SendState();Toast("success","Model removed.");}
        private void QueueBrowseVoice(Dictionary<string,object> payload)
        {
            if(_voiceDialogOpen||_closing)return;_voiceDialogOpen=true;string modelId=GetString(payload,"modelId"),name=GetString(payload,"name"),description=GetString(payload,"description"),transcript=GetString(payload,"transcript");
            try{BeginInvoke(new Action(async()=>{try{using(var d=new OpenFileDialog()){d.Title="Choose a voice reference";d.Filter="Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus|All files|*.*";d.Multiselect=false;if(d.ShowDialog(this)!=DialogResult.OK)return;await ImportAndPrepareVoiceAsync(d.FileName,name,description,modelId,transcript);}}catch(Exception ex){PostError(ex);}finally{_voiceDialogOpen=false;}}));}catch{_voiceDialogOpen=false;}
        }
        private async Task ImportAudioDataAsync(Dictionary<string,object> payload)
        {
            string data=GetString(payload,"data"),fileName=GetString(payload,"fileName","reference.wav"),modelId=GetString(payload,"modelId"),name=GetString(payload,"name"),description=GetString(payload,"description"),transcript=GetString(payload,"transcript");if(string.IsNullOrWhiteSpace(data))throw new InvalidDataException("No audio data was received.");int comma=data.IndexOf(',');if(comma<0)throw new InvalidDataException("Invalid audio data.");byte[] bytes=Convert.FromBase64String(data.Substring(comma+1));if(bytes.Length>64*1024*1024)throw new InvalidDataException("Audio file is too large. Use Browse for files over 64 MB.");string ext=Path.GetExtension(fileName);if(string.IsNullOrWhiteSpace(ext))ext=".wav";string tmp=Path.Combine(AppPaths.Temp,"voice-"+Guid.NewGuid().ToString("N")+ext);File.WriteAllBytes(tmp,bytes);try{await ImportAndPrepareVoiceAsync(tmp,name,description,modelId,transcript);}finally{try{File.Delete(tmp);}catch{}};
        }
        private async Task ImportAndPrepareVoiceAsync(string file,string name,string description,string modelId,string transcript)
        {
            if(!TryStartOperation("Analyzing and caching the new voice…"))return;try{var v=await _voices.ImportAndPrepareAsync(file,name,description,modelId,transcript,CreateProgress(),_operationCts.Token,CreateEngineLineSink());Post("voiceImported",v);SendState();Toast("success","Voice analyzed and cached locally.");}catch(OperationCanceledException){Toast("info","Voice import cancelled.");}finally{EndOperation();}
        }
        private async Task PreviewVoiceAsync(string id)
        {
            var v=_voices.Find(id);if(v==null)return;if(v.Kind=="custom"&&File.Exists(v.FilePath)){string rel=MakeRelativePath(AppPaths.Voices,v.FilePath).Replace('\\','/');Post("voicePreview",new{url="https://voices.voxena/"+string.Join("/",rel.Split('/').Select(Uri.EscapeDataString))});return;}Toast("info","Generate a short sentence to preview this built-in voice.");await Task.CompletedTask;
        }
        private async Task GenerateAsync(Dictionary<string,object> payload)
        {
            if(!TryStartOperation("Generating two voice variants…"))return;
            try
            {
                var r=new GenerationRequest{Text=GetString(payload,"text"),VoiceId=GetString(payload,"voiceId"),Stability=GetDouble(payload,"stability",.55),Speed=GetDouble(payload,"speed",1),Pitch=GetDouble(payload,"pitch",0),Expressiveness=GetDouble(payload,"expressiveness",.5),Format=GetString(payload,"format","mp3"),SampleRate=GetInt(payload,"sampleRate",44100),BitrateKbps=GetInt(payload,"bitrateKbps",192),Normalize=GetBool(payload,"normalize",true),TrimSilence=GetBool(payload,"trimSilence",true),Seed=GetInt(payload,"seed",0)};
                _settings.SelectedVoiceId=r.VoiceId;_settings.Stability=r.Stability;_settings.Speed=r.Speed;_settings.Pitch=r.Pitch;_settings.OutputFormat=r.Format;_settings.SampleRate=r.SampleRate;_settings.BitrateKbps=r.BitrateKbps;_settings.Normalize=r.Normalize;_settings.TrimSilence=r.TrimSilence;_settingsStore.Save(_settings);

                int seedA=r.Seed==0?CreateRandomSeed():NormalizeSeed(r.Seed);
                int seedB=seedA==int.MaxValue?1:seedA+1;
                if(seedB==seedA||seedB==0)seedB=seedA>1?seedA-1:2;
                _lastGenerations.Clear();
                Post("generationReset",new{});
                var results=await _tts.GenerateVariantsAsync(r,_settings,new[]{seedA,seedB},CreateProgress(),_operationCts.Token,CreateEngineLineSink());
                var failed=results.FirstOrDefault(x=>x==null||!x.Success);
                if(failed!=null)
                {
                    string err=failed==null?"Generation failed.":failed.Error;
                    if(err=="Cancelled.")Toast("info","Generation cancelled.");else PostError(err,err);
                    return;
                }
                _lastGenerations.Clear();_lastGenerations.AddRange(results);
                Post("generationComplete",new{results=results});
                if(_settings.OpenOutputAfterGeneration&&results.Count>0)RevealFile(results[0].FilePath);
            }
            finally{EndOperation();}
        }

        private static int CreateRandomSeed()
        {
            var bytes=new byte[4];
            using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
            int value=BitConverter.ToInt32(bytes,0)&0x7fffffff;
            return value==0?1:value;
        }
        private static int NormalizeSeed(int value){if(value==0)return 1;if(value==int.MinValue)return int.MaxValue;return Math.Abs(value);}
        private void HandleTitlebar(string command){switch((command??"").ToLowerInvariant()){case "minimize":WindowState=FormWindowState.Minimized;break;case "maximize":case "togglemaximize":ToggleMaximize();break;case "close":Close();break;case "drag":if(WindowState==FormWindowState.Maximized)return;NativeMethods.ReleaseCapture();NativeMethods.SendMessage(Handle,NativeMethods.WM_NCLBUTTONDOWN,(IntPtr)NativeMethods.HTCAPTION,IntPtr.Zero);break;}}
        private void ToggleMaximize(){WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;if(WindowState==FormWindowState.Maximized)MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;SendWindowState();}
        private void SaveSettingsFromPayload(Dictionary<string,object> payload){object obj;if(payload==null||!payload.TryGetValue("settings",out obj)||obj==null)return;var updated=_json.Deserialize<AppSettings>(_json.Serialize(obj));if(updated==null)return;if(string.IsNullOrWhiteSpace(updated.OutputFolder))updated.OutputFolder=AppPaths.Output;_settings=updated;_settingsStore.Save(_settings);try{_web.CoreWebView2.Settings.AreDevToolsEnabled=_settings.EnableDevTools;_web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled=_settings.EnableDevTools;}catch{}ApplyNativeTheme();}
        private AppStateDto BuildState(){var profiles=_models.GetProfiles();foreach(var p in profiles)p.Recommended=_hardware.VramMb>0&&_hardware.VramMb>=p.RecommendedVramMb&&(_hardware.VramMb-p.RecommendedVramMb)<5000;return new AppStateDto{Settings=_settings,Hardware=_hardware,Voices=_voices.GetAll(),Profiles=profiles,Version=Application.ProductVersion,StressModelReady=_runtime.StressReady};}
        private void SendState(){if(!_webReady)return;Post("state",new{app=BuildState()});}
        private Action<string> CreateEngineLineSink(){return QueueEngineLine;}
        private void QueueEngineLine(string text)
        {
            if(_closing||string.IsNullOrWhiteSpace(text))return;
            lock(_engineLineGate)_pendingEngineLine=text;
        }
        private void FlushEngineLine()
        {
            string text=null;
            lock(_engineLineGate){text=_pendingEngineLine;_pendingEngineLine=null;}
            if(!string.IsNullOrWhiteSpace(text))Post("engineLine",new{text=text});
        }
        private IProgress<DownloadProgress> CreateProgress(){return new Progress<DownloadProgress>(x=>Post("downloadProgress",x));}
        private bool TryStartOperation(string text){if(_operationCts!=null){Toast("info","Another operation is already running.");return false;}_operationCts=new CancellationTokenSource();Post("busy",new{active=true,text=text});return true;}
        private void EndOperation(){var c=_operationCts;_operationCts=null;try{if(c!=null)c.Dispose();}catch{}Post("busy",new{active=false,text=""});}
        private GenerationResult GetGeneration(int index){return index>=0&&index<_lastGenerations.Count?_lastGenerations[index]:null;}
        private void SaveGeneratedAs(int index){var g=GetGeneration(index);if(g==null||!File.Exists(g.FilePath))throw new InvalidOperationException("Generate audio first.");using(var d=new SaveFileDialog()){string ext=Path.GetExtension(g.FilePath);d.FileName=Path.GetFileName(g.FilePath);d.Filter="Audio file|*"+ext+"|All files|*.*";if(Directory.Exists(_settings.OutputFolder))d.InitialDirectory=_settings.OutputFolder;if(d.ShowDialog(this)!=DialogResult.OK)return;File.Copy(g.FilePath,d.FileName,true);Toast("success",Local("Variant saved.","Вариант сохранён.","Варіант збережено."));}}
        private void RevealGenerated(int index){var g=GetGeneration(index);if(g!=null)RevealFile(g.FilePath);}
        private void PickOutputFolder(){using(var d=new FolderBrowserDialog()){d.Description="Choose Voxena output folder";d.SelectedPath=Directory.Exists(_settings.OutputFolder)?_settings.OutputFolder:AppPaths.Output;if(d.ShowDialog(this)!=DialogResult.OK)return;_settings.OutputFolder=d.SelectedPath;_settingsStore.Save(_settings);SendState();}}
        private void Toast(string kind,string message){Post("toast",new{kind=kind,message=message});}private void PostError(Exception ex){PostError(ex==null?"Unknown error.":ex.Message,ex==null?"Unknown error.":ex.ToString());}private void PostError(string message,string detail=null){Post("error",new{message=message,detail=string.IsNullOrWhiteSpace(detail)?message:detail});}
        private void Post(string type,object data){if(_closing||!_webReady||_web.CoreWebView2==null)return;if(InvokeRequired){try{BeginInvoke(new Action(()=>Post(type,data)));}catch{}return;}try{_web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(new{type=type,data=data}));}catch(Exception ex){Logger.Write("Post failed: "+ex.Message);}}
        private void SendWindowState(){Post("windowState",new{maximized=WindowState==FormWindowState.Maximized,minimized=WindowState==FormWindowState.Minimized});}
        private void ApplyNativeTheme(){bool dark=!string.Equals(_settings.Theme,"light",StringComparison.OrdinalIgnoreCase);BackColor=dark?Color.FromArgb(8,12,24):Color.FromArgb(245,247,252);NativeMethods.ApplyWindowAppearance(this,dark);}
        private Icon LoadIconSafe(){try{string p=Path.Combine(AppPaths.Assets,"app.ico");return File.Exists(p)?new Icon(p):null;}catch{return null;}}
        private void OnFormClosing(object sender,FormClosingEventArgs e){_closing=true;try{_engineLineTimer.Stop();_engineLineTimer.Dispose();}catch{}try{if(_operationCts!=null)_operationCts.Cancel();}catch{}try{_downloads.Dispose();}catch{}try{AppPaths.ClearGeneratedCache();}catch{}}
        private static void OpenExternal(string url){Uri u;if(string.IsNullOrWhiteSpace(url)||!Uri.TryCreate(url,UriKind.Absolute,out u)||(u.Scheme!="http"&&u.Scheme!="https"))return;try{Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});}catch{}}
        private static void OpenFolder(string path){if(string.IsNullOrWhiteSpace(path))return;try{Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch{}}private static void RevealFile(string path){if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return;try{Process.Start("explorer.exe","/select,"+ProcessRunner.Quote(path));}catch{}}
        private static string MakeRelativePath(string root,string path){Uri a=new Uri(AppendSlash(Path.GetFullPath(root))),b=new Uri(Path.GetFullPath(path));return Uri.UnescapeDataString(a.MakeRelativeUri(b).ToString()).Replace('/',Path.DirectorySeparatorChar);}private static string AppendSlash(string p){return p.EndsWith(Path.DirectorySeparatorChar.ToString())?p:p+Path.DirectorySeparatorChar;}
        private string Local(string en,string ru,string ua){string l=(_settings.Language??"en").ToLowerInvariant();return l=="ru"?ru:(l=="ua"||l=="uk"?ua:en);}
        private static string GetString(Dictionary<string,object> p,string k,string f=""){object v;return p!=null&&p.TryGetValue(k,out v)&&v!=null?(Convert.ToString(v)??f):f;}private static int GetInt(Dictionary<string,object> p,string k,int f){object v;int n;return p!=null&&p.TryGetValue(k,out v)&&v!=null&&int.TryParse(Convert.ToString(v),out n)?n:f;}private static double GetDouble(Dictionary<string,object> p,string k,double f){object v;double n;return p!=null&&p.TryGetValue(k,out v)&&v!=null&&double.TryParse(Convert.ToString(v),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out n)?n:f;}private static bool GetBool(Dictionary<string,object> p,string k,bool f){object v;bool b;return p!=null&&p.TryGetValue(k,out v)&&v!=null&&bool.TryParse(Convert.ToString(v),out b)?b:f;}
        private static string[] GetStringArray(Dictionary<string,object> p,string k){object v;if(p==null||!p.TryGetValue(k,out v)||v==null)return new string[0];var list=new List<string>();var enumerable=v as IEnumerable;if(enumerable!=null&&!(v is string)){foreach(var x in enumerable)if(x!=null)list.Add(Convert.ToString(x));}else list.Add(Convert.ToString(v));return list.ToArray();}
    }
}
