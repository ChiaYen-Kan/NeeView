﻿using NeeView.Interop;
using NeeView.Native;
using NeeView.Properties;
using NeeView.Text.SimpleHtmlBuilder;
using NeeView.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NeeView
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private static App? _current;
        public static new App Current => _current ?? throw new InvalidOperationException("_current must not be null");


        // Fields

        private bool _isSplashScreenVisible;
        private bool _isTerminated;
        private readonly int _tickBase = System.Environment.TickCount;
        private CommandLineOption? _option;
        private MultiBootService? _multiBootService;


        // Properties

        // オプション設定
        public CommandLineOption Option => _option ?? throw new InvalidOperationException("_option must not be null");

        // システムロック
        public System.Threading.Lock Lock { get; } = new();

        // 起動日時
        public DateTime StartTime { get; private set; }

        // 開発用：ストップウォッチ
        public Stopwatch Stopwatch { get; private set; } = new();

        // MainWindowはLoad完了している？
        public bool IsMainWindowLoaded { get; set; }


        /// <summary>
        /// アプリの起動時間(ms)取得
        /// </summary>
        public int TickCount => System.Environment.TickCount - _tickBase;



        /// <summary>
        /// Startup
        /// </summary>
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            _current = this;

            StartTime = DateTime.Now;
            Stopwatch.Start();

            // DLL 検索パスから現在の作業ディレクトリ (CWD) を削除
            NativeMethods.SetDllDirectory("");

#if TRACE_LOG
            var nowTime = DateTime.Now;
            var traceLogFilename = $"Trace{nowTime.ToString("yyMMdHHmmss")}.log";
            StreamWriter sw = new StreamWriter(traceLogFilename) { AutoFlush = true };
            TextWriterTraceListener twtl = new TextWriterTraceListener(TextWriter.Synchronized(sw), "MyListener");
            Trace.Listeners.Add(twtl);
            Trace.WriteLine($"Trace: Start ({nowTime})");
#endif

            // 起動処理排他ロック取得。時間内 (30sec) に獲得できなければアプリ終了
            var bootLock = new BootProcessLock(30 * 1000);
            var bootLockKey = bootLock.Lock();

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // [開発用] ログ出力設定
                if (!string.IsNullOrEmpty(Environment.LogFile))
                {
                    var twtl = new TextWriterTraceListener(Environment.LogFile, "TraceLog");
                    Trace.Listeners.Add(twtl);
                    Trace.AutoFlush = true;
                    Trace.WriteLine(System.Environment.NewLine + new string('=', 80));
                }

                Trace.WriteLine($"App.Startup: PID={System.Environment.ProcessId}: {DateTime.Now}");

                // 未処理例外ハンドル
                InitializeUnhandledException();

                await InitializeAsync(e);
            }
            catch (OperationCanceledException ex)
            {
                Trace.WriteLine("InitializeCancelException: " + ex.Message);
                bootLockKey.Dispose();

                if (ex is OperationCanceledWithDialogException dialogException)
                {
                    dialogException.ShowDialog();
                }

                ShutdownWithoutSave();
                return;
            }
            finally
            {
                bootLockKey.Dispose();
                bootLock.Dispose();
            }

            Trace.WriteLine($"App.Initialized: {Stopwatch.ElapsedMilliseconds}ms");

            // ToolTipService default properties
            ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(0));

            this.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // メインウィンドウ起動
            var mainWindow = new MainWindow();
            WindowParameters.Initialize(mainWindow);

            NVInterop.NVFpReset();
            mainWindow.Show();

            MessageDialog.IsShowInTaskBar = false;
        }

        /// <summary> 
        /// 初期化 
        /// </summary>
        private async ValueTask InitializeAsync(StartupEventArgs e)
        {
            Debug.WriteLine($"App.InitializeAsync...: {Stopwatch.ElapsedMilliseconds}ms");

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // AnimatedImage ライブラリ初期化
            AnimatedImageChecker.InitializeLibrary();

            // APPXデータフォルダ移動 (ver.38)
            Environment.CorrectLocalAppDataFolder();

            // コマンドライン引数処理
            _option = ParseCommandLineOption(e.Args);

            // カレントディレクトリを実行ファイルの場所に変更。ファイルロック回避のため
            System.IO.Directory.SetCurrentDirectory(Environment.AssemblyFolder);

            // 多重起動サービス起動
            _multiBootService = new MultiBootService();

            // セカンドプロセス判定
            Environment.IsSecondProcess = _multiBootService.IsServerExists;

            DebugStamp("UserSettingLoading...");

            // load UserSetting bytes
            UserSetting? setting = null;
            var settingResource = new UserSettingResource(_option.SettingFilename);

            // create boot setting
            var boot = settingResource.LoadBootSetting();
            if (boot is null)
            {
                setting = CreateUserSetting(settingResource);
                Debug.Assert(setting.Config is not null);
                boot = BootSetting.Create(setting.Config);
            }

            // If multiple launches are not possible, send parameters to the main app to terminate.
            if (!CanStart(boot.IsMultiBootEnabled))
            {
                await _multiBootService.RemoteLoadAsAsync(Option.Values);
                throw new OperationCanceledException("Already started.");
            }

            // show splash screen
            ShowSplashScreen(boot);

            // initialize before UserSetting
            var language = _option.Language ?? boot.Language;
            InitializeTextResource(language);
            InitializeHtmlNode();
            InitializeCommandTable();

            // ensure UserSetting
            setting ??= CreateUserSetting(settingResource);
            SaveData.Current.SetUserSettingFileStamp(setting.FileStamp);
            UserSettingTools.Restore(setting, replaceConfig: true);

            // fix language
            if (_option.Language is null)
            {
                Config.Current.System.Language = TextResources.Culture.Name;
            }

            DebugStamp("UserSettingLoaded");

            // show version dialog
            if (this.Option.IsVersion)
            {
                ShowVersionDialog();
                throw new OperationCanceledException("Displayed version dialog.");
            }

            // initialize after UserSetting
            InitializeSupportFileType(Config.Current);
            InitializeTemporary(Config.Current);
            InitializeImeKey(Config.Current);
            InitializeTheme();
        }

        /// <summary>
        /// コマンドライン引数処理
        /// </summary>
        /// <returns></returns>
        private CommandLineOption ParseCommandLineOption(string[] args)
        {
            // コマンドライン引数処理
            var option = ParseArguments(args);
            option.Validate();

            // シフトキー起動は新しいウィンドウで
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                option.IsNewWindow = SwitchOption.on;
            }

            return option;
        }

        /// <summary>
        /// UserSetting 生成
        /// </summary>
        private UserSetting CreateUserSetting(UserSettingResource settingResource)
        {
            using var span = DebugSpan();
            try
            {
                var setting = settingResource.Load() ?? new UserSetting();
                return setting.EnsureConfig();
            }
            catch (Exception ex)
            {
                var dialog = new UserSettingLoadFailedDialog(true);
                var result = dialog.ShowDialog(ex);
                if (result != true)
                {
                    throw new OperationCanceledException();
                }
                return new UserSetting().EnsureConfig();
            }
        }

        /// <summary>
        /// バージョンダイアログを表示
        /// </summary>
        private void ShowVersionDialog()
        {
            var dialog = new VersionWindow() { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            dialog.ShowDialog();
        }

        /// <summary>
        /// 言語リソース初期化
        /// </summary>
        /// <param name="language"></param>
        private void InitializeTextResource(string language)
        {
            using var span = DebugSpan();
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.CurrentCulture;
            }
            TextResources.Initialize(culture);
            InputGestureDisplayString.Initialize(TextResources.Resource);

            // 開発用：テキストリソース検証
            //TextResources.ValidateAllCultures();
        }

        /// <summary>
        /// ヘルプ用 HtmlNode 初期化
        /// </summary>
        private void InitializeHtmlNode()
        {
            HtmlNode.DefaultTextEvaluator = e => TextResources.Replace(e, true);
        }

        /// <summary>
        /// コマンドテーブル初期化
        /// </summary>
        private void InitializeCommandTable()
        {
            using var span = DebugSpan();
            _ = CommandTable.Current;
        }

        /// <summary>
        /// 画像拡張子初期化
        /// </summary>
        private void InitializeSupportFileType(Config config)
        {
            using var span = DebugSpan();
            if (config.Image.Standard.SupportFileTypes is null)
            {
                config.Image.Standard.SupportFileTypes = PictureFileExtensionTools.CreateDefaultSupportedFileTypes(config.Image.Standard.UseWicInformation);
            }
        }

        /// <summary>
        /// テンポラリーの場所
        /// </summary>
        private void InitializeTemporary(Config config)
        {
            using var span = DebugSpan();
            config.System.TemporaryDirectory = Temporary.Current.SetDirectory(config.System.TemporaryDirectory, true);
        }

        /// <summary>
        /// TextBox以外のコントロールのIMEキー設定
        /// </summary>
        private void InitializeImeKey(Config config)
        {
            using var span = DebugSpan();
            if (!config.System.IsInputMethodEnabled)
            {
                InputMethod.IsInputMethodEnabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(false));
                InputMethod.IsInputMethodEnabledProperty.OverrideMetadata(typeof(System.Windows.Controls.TextBox), new FrameworkPropertyMetadata(true));
            }
        }

        /// <summary>
        /// テーマの初期化
        /// </summary>
        private void InitializeTheme()
        {
            using var span = DebugSpan();
            _ = ThemeManager.Current;
        }

        /// <summary>
        /// Show SplashScreen
        /// </summary>
        public void ShowSplashScreen(BootSetting bootSetting)
        {
            if (bootSetting.IsSplashScreenEnabled && CanStart(bootSetting.IsMultiBootEnabled) && !this.Option.IsVersion)
            {
                if (_isSplashScreenVisible) return;
                _isSplashScreenVisible = true;

                using var span = DebugSpan();
                var resourceName = "Resources/SplashScreen.png";
                var splashScreen = new SplashScreen(resourceName);
#if DEBUG
                splashScreen.Show(true);
#else
                splashScreen.Show(true, true);
#endif
            }
        }

        /// <summary>
        /// 多重起動用実行可能判定
        /// </summary>
        private bool CanStart(bool isMultiBootEnabled)
        {
            if (_multiBootService is null) return false;

            return !_multiBootService.IsServerExists || (Option.IsNewWindow != null ? Option.IsNewWindow == SwitchOption.on : isMultiBootEnabled);
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Trace.WriteLine("App.Exit:");
            Terminate(true);
        }

        /// <summary>
        /// PCシャットダウン時に呼ばれる
        /// </summary>
        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            Trace.WriteLine($"App.SessionEnding: {e.ReasonSessionEnding}");

            (this.MainWindow as MainWindow)?.FinalizeMainWindow();

            Terminate(false);
        }

        /// <summary>
        /// 終了時処理。設定の保存等
        /// </summary>
        private void Terminate(bool callProcessTerminator)
        {
            if (_isTerminated) return;
            _isTerminated = true;

            TerminateUnhandledException();

            try
            {
                // 現在のブックを履歴に保存
                PageFrameBoxPresenter.Current.ForceSaveBookMemento();
                PageFrameBoxPresenter.Current.SaveLastBookMemento();

                // 現在の本棚の場所を保存
                BookshelfFolderList.Current.SaveLastFolderPath();

                // 各種Dispose
                ApplicationDisposer.Current.Dispose();

                if (App.Current.IsMainWindowLoaded)
                {
                    // 設定保存
                    SaveDataSync.Current.DisposeWatcher();
                    SaveDataSync.Current.SaveAll(false, false);
                    SaveDataSync.Current.Dispose();
                    SaveData.Current.DisableSave();

                    // キャッシュDBのクリーンナップ
                    ThumbnailCache.Current.Cleanup();

                    // キャッシュDBを閉じる
                    ThumbnailCache.Current.Dispose();

                    // テンポラリファイル破棄
                    Temporary.Current.RemoveTempFolder();
                }

                Trace.WriteLine($"App.Terminate: {DateTime.Now}: Terminated.");
            }
            catch (Exception ex)
            {
                Debug.Assert(false, "Application Terminate failed!!");
                Trace.Fail($"App.Terminate: {DateTime.Now}: {ex.ToStackString()}");
                throw;
            }

            if (callProcessTerminator)
            {
                try
                {
                    CallProcessTerminator();
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, "Cannot start Terminator.");
                    Trace.Fail($"Cannot start Terminator: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 自プロセスがゾンビ化しても停止させる処理。
        /// 現象ではPdfDocumentによってゾンビプロセス化してしまうため。
        /// </summary>
        /// <seealso href="https://github.com/microsoft/CsWinRT/issues/1249"/>
        private static void CallProcessTerminator()
        {
            // NOTE: PdfDocument を使用していないので不要
#if false
            // NOTE: PDFでWinRTのレンダラーを使用している場合のみ機能させる
            if (PdfArchiveConfig.GetPdfRenderer() != PdfRenderer.WinRT) return;

            var filename = Path.Combine(Environment.LibrariesPath, "Libraries\\NeeView.Terminator.exe");

            // 開発中は直接プロジェクトを参照する
            if (Environment.IsDevPackage)
            {
                filename = Path.GetFullPath(Path.Combine(Environment.AssemblyFolder, @"..\..\..\..\..\NeeView.Terminator\bin", Environment.PlatformName, Environment.ConfigType, @"net9.0\NeeView.Terminator.exe"));
            }

            // 5秒後にこのプロセスが残っていたら強制終了させる監視プロセスを発行
            int timeout = 5000;
            var process = Process.GetCurrentProcess();
            var info = new ProcessStartInfo();
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.FileName = filename;
            info.Arguments = $"{process.Id} \"{process.ProcessName}\" {process.StartTime.ToFileTime()} {timeout}";
            Process.Start(info);
#endif
        }


        /// <summary>
        /// 保存しないでアプリをシャットダウン
        /// </summary>
        public void ShutdownWithoutSave()
        {
            SaveData.Current.DisableSave();
            Shutdown();
        }


        #region Develop

        private IDisposable? DebugSpan([CallerMemberName] string callerMethodName = "")
        {
#if DEBUG
            return new DebugSpanScope(Stopwatch, "App." + callerMethodName);
#else
            return null;
#endif
        }

        [Conditional("DEBUG")]
        private void DebugStamp(string label)
        {
            DebugSpanScope.Dump(Stopwatch, "App." + label);
        }

        #endregion Develop
    }

}
