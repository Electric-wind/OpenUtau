using System;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class TrackSettingsViewModel : ViewModelBase {
        public UTrack Track { get; private set; }
        public ObservableCollectionExtended<IResampler> Resamplers => resamplers;
        [Reactive] public IResampler? Resampler { get; set; }
        [Reactive] public bool NeedsResampler { get; set; }
        public ObservableCollectionExtended<IWavtool> Wavtools => wavtools;
        [Reactive] public IWavtool? Wavtool { get; set; }
        [Reactive] public bool NeedsWavtool { get; set; }
        [Reactive] public bool IsNotClassic { get; set; }
        [Reactive] public bool IsCustomServer { get; set; }
        [Reactive] public string ServerUrl { get; set; } = "http://localhost:8000";
        [Reactive] public string Endpoint { get; set; } = "/synthesize";

        ObservableCollectionExtended<IResampler> resamplers =
            new ObservableCollectionExtended<IResampler>();
        ObservableCollectionExtended<IWavtool> wavtools =
            new ObservableCollectionExtended<IWavtool>();

        public TrackSettingsViewModel(UTrack track) {
            ToolsManager.Inst.Initialize();
            Track = track;
            if (!string.IsNullOrEmpty(Track.RendererSettings.renderer)) {
                var renderer = Track.RendererSettings.renderer;
                resamplers.AddRange(ToolsManager.Inst.Resamplers);
                string? resamplerName = Track.RendererSettings.resampler;
                if (string.IsNullOrEmpty(resamplerName)) {
                    if (!Preferences.Default.DefaultResamplers.TryGetValue(renderer, out resamplerName)) {
                        resamplerName = string.Empty;
                    }
                }
                Resampler = ToolsManager.Inst.GetResampler(resamplerName);
                wavtools.AddRange(Renderers.GetSupportedWavtools(Resampler));
                string? wavtoolName = Track.RendererSettings.wavtool;
                if (string.IsNullOrEmpty(wavtoolName)) {
                    if (!Preferences.Default.DefaultWavtools.TryGetValue(renderer, out wavtoolName)) {
                        wavtoolName = string.Empty;
                    }
                }
                Wavtool = ToolsManager.Inst.GetWavtool(wavtoolName);
                NeedsResampler = Renderers.CLASSIC == renderer;
                NeedsWavtool = Renderers.CLASSIC == renderer;
                IsNotClassic = Renderers.CLASSIC != renderer;
                IsCustomServer = Renderers.CUSTOM_SERVER == renderer;
                ServerUrl = Track.RendererSettings.serverUrl ?? Preferences.Default.DefaultServerUrl;
                Endpoint = Track.RendererSettings.endpoint ?? Preferences.Default.DefaultEndpoint;
            }
            this.WhenAnyValue(x => x.Resampler)
                .Subscribe(resampler => {
                    resampler?.CheckPermissions();
                    var wavtool = Wavtool;
                    wavtools.Clear();
                    wavtools.AddRange(Renderers.GetSupportedWavtools(resampler));
                    if (wavtool != null && wavtools.Contains(wavtool)) {
                        Wavtool = wavtool;
                    } else {
                        Wavtool = wavtools.FirstOrDefault();
                    }
                });
            this.WhenAnyValue(x => x.Wavtool)
                .Subscribe(wavtool => {
                    wavtool?.CheckPermissions();
                });
        }

        public void OpenResamplerLocation() {
            OS.OpenFolder(PathManager.Inst.ResamplersPath);
        }

        public void SetDefaultResampler() {
            if (Resampler != null) {
                Preferences.Default.DefaultResamplers[Track.RendererSettings.renderer] = Resampler.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void OpenWavtoolLocation() {
            OS.OpenFolder(PathManager.Inst.WavtoolsPath);
        }

        public void SetDefaultWavtool() {
            if (Wavtool != null) {
                Preferences.Default.DefaultWavtools[Track.RendererSettings.renderer] = Wavtool.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void SetDefaultServer() {
            if (!string.IsNullOrEmpty(ServerUrl)) {
                Preferences.Default.DefaultServerUrl = ServerUrl;
            }
            if (!string.IsNullOrEmpty(Endpoint)) {
                Preferences.Default.DefaultEndpoint = Endpoint;
            }
            Preferences.Save();
        }

        public void Finish() {
            DocManager.Inst.StartUndoGroup("command.track.setting");
            var settings = Track.RendererSettings.Clone();
            if (Renderers.CUSTOM_SERVER == Track.RendererSettings.renderer) {
                settings.serverUrl = ServerUrl;
                settings.endpoint = Endpoint;
            }
            if (Renderers.CLASSIC == Track.RendererSettings.renderer) {
                settings.resampler = Resampler?.ToString() ?? string.Empty;
                settings.wavtool = Wavtool?.ToString() ?? string.Empty;
            }
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, Track, settings));
            DocManager.Inst.EndUndoGroup();
        }
    }
}
