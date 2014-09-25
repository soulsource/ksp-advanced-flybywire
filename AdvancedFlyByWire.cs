﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace KSPAdvancedFlyByWire
{

    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class AdvancedFlyByWire : MonoBehaviour
    {
        private KSP.IO.PluginConfiguration m_Config;
        private IController m_Controller = null;

        private List<ControllerPreset> m_Presets = new List<ControllerPreset>();
        private int m_CurrentPreset = 0;

        private float m_DiscreteActionStep = 0.15f;
        private CurveType m_AnalogInputCurveType = CurveType.XSquared;
        private float m_IncrementalThrottleSensitivity = 0.05f;

        private float m_Throttle = 0.0f;
        private bool m_CallbackSet = false;

        private CameraManager.CameraMode m_OriginalCameraMode;

        private ControllerPreset GetCurrentPreset()
        {
            var preset = m_Presets[m_CurrentPreset];
            if(preset == null)
            {
                print("invalid preset");
            }
            
            return preset;
        }

        void SetAnalogInputCurveType(CurveType type)
        {
            m_AnalogInputCurveType = type;
            m_Controller.analogEvaluationCurve = CurveFactory.Instantiate(type);
        }

        private void SavePresetsToDisk()
        {
            m_Config.SetValue("AnalogInputCurveType", m_AnalogInputCurveType);

            m_Config.SetValue("PresetsCount", m_Presets.Count);
            m_Config.SetValue("SelectedPreset", m_CurrentPreset);

          /*  for (int i = 0; i < m_Presets.Count; i++)
            {
                m_Config.SetValue("Preset" + i, m_Presets[i]);
            }*/

            m_Config.save();
        }

        public void Awake()
        {
            print("KSPAdvancedFlyByWire: initialized");

            m_Config = KSP.IO.PluginConfiguration.CreateForType<AdvancedFlyByWire>();
            m_Config.load();

            m_AnalogInputCurveType = m_Config.GetValue<CurveType>("AnalogInputCurveType", CurveType.XSquared);

            m_Controller = new XInputController();
            m_Controller.analogEvaluationCurve = CurveFactory.Instantiate(m_AnalogInputCurveType);
            m_Controller.buttonPressedCallback = new XInputController.ButtonPressedCallback(ButtonPressedCallback);
            m_Controller.buttonReleasedCallback = new XInputController.ButtonReleasedCallback(ButtonReleasedCallback);

            m_Presets = DefaultControllerPresets.GetDefaultPresets(m_Controller);
            m_CurrentPreset = 0;
        }

        public void OnDestroy()
        {
        }

        void DoMainWindow(int index)
        {
            string buttonsMask = "";

            for (int i = 0; i < m_Controller.GetButtonsCount(); i++)
            {
                buttonsMask = (m_Controller.GetButtonState(i) ? "1" : "0") + buttonsMask;
            }

            GUILayout.Label(buttonsMask);
            GUILayout.Label(m_Controller.GetButtonsMask().ToString());

            for (int i = 0; i < m_Controller.GetAxesCount(); i++)
            {
                string label = "";
                label += m_Controller.GetAxisName(i) + " ";
                label += m_Controller.GetAnalogInputState(i);
                GUILayout.Label(label);
            }
        }

        private HashSet<int> m_EvaluatedDiscreteActionMasks = new HashSet<int>();

        void ButtonPressedCallback(int button, FlightCtrlState state)
        {
            int mask = m_Controller.GetButtonsMask();

            if(m_EvaluatedDiscreteActionMasks.Contains(mask))
            {
                return;
            }

            DiscreteAction action = GetCurrentPreset().GetDiscreteBinding(mask);
            if(action != DiscreteAction.None)
            {
                EvaluateDiscreteAction(action, state);
                m_EvaluatedDiscreteActionMasks.Add(mask);
            }
        }

        void ButtonReleasedCallback(int button, FlightCtrlState state)
        {
            List<int> masksToRemove = new List<int>();

            foreach(int evaluatedMask in m_EvaluatedDiscreteActionMasks)
            {
                for(int i = 0; i < m_Controller.GetButtonsCount(); i++)
                {
                   bool maskHasBitSet = (evaluatedMask & (1 << i)) != 0;

                   if (!m_Controller.GetButtonState(i) && maskHasBitSet)
                   {
                       masksToRemove.Add(evaluatedMask);
                       break;
                   }
                }
            }

            foreach(int maskRemove in masksToRemove)
            {
                m_EvaluatedDiscreteActionMasks.Remove(maskRemove);
            }

            EvaluateDiscreteActionRelease(GetCurrentPreset().GetDiscreteBinding(m_Controller.GetButtonsMask()), state);
        }

        private void OnFlyByWire(FlightCtrlState state)
        {
            FlightGlobals.ActiveVessel.VesselSAS.ManualOverride(true);

            m_Controller.Update(state);
            state.mainThrottle = m_Throttle;

            for (int i = 0; i < m_Controller.GetAxesCount(); i++)
            {
                List<ContinuousAction> actions = GetCurrentPreset().GetContinuousBinding(i, m_Controller.GetButtonsMask());
                if(actions == null)
                {
                    continue;
                }

                foreach (var action in actions)
                {
                    float input = m_Controller.GetAnalogInputState(i);
                    if(input != 0.0f)
                    {
                        EvaluateContinuousAction(action, m_Controller.GetAnalogInputState(i), state);
                    }
                }
            }

           FlightGlobals.ActiveVessel.VesselSAS.ManualOverride(false);
        }
        private float Clamp(float x, float min, float max)
        {
            return x < min ? min : x > max ? max : x;
        }
        
        private void EvaluateDiscreteAction(DiscreteAction action, FlightCtrlState state)
        {
            KerbalEVA eva = null;
            if (FlightGlobals.ActiveVessel.isEVA)
            {
                eva = FlightGlobals.ActiveVessel.GetComponent<KerbalEVA>();
            }

            switch (action)
            {
            case DiscreteAction.None:
                return;
            case DiscreteAction.YawPlus:
                state.yaw += m_DiscreteActionStep;
                state.yaw = Clamp(state.yaw, -1.0f, 1.0f);
                return;
            case DiscreteAction.YawMinus:
                state.yaw -= m_DiscreteActionStep;
                state.yaw = Clamp(state.yaw, -1.0f, 1.0f);
                return;
            case DiscreteAction.PitchPlus:
                state.pitch += m_DiscreteActionStep;
                state.pitch = Clamp(state.pitch, -1.0f, 1.0f);
                return;
            case DiscreteAction.PitchMinus:
                state.pitch -= m_DiscreteActionStep;
                state.pitch = Clamp(state.pitch, -1.0f, 1.0f);
                return;
            case DiscreteAction.RollPlus:
                state.roll += m_DiscreteActionStep;
                state.roll = Clamp(state.roll, -1.0f, 1.0f);
                return;
            case DiscreteAction.RollMinus:
                state.roll -= m_DiscreteActionStep;
                state.roll = Clamp(state.roll, -1.0f, 1.0f);
                return;
            case DiscreteAction.XPlus:
                state.X += m_DiscreteActionStep;
                state.X = Clamp(state.X, -1.0f, 1.0f);
                return;
            case DiscreteAction.XMinus:
                state.X -= m_DiscreteActionStep;
                state.X = Clamp(state.X, -1.0f, 1.0f);
                return;
            case DiscreteAction.YPlus:
                state.Y += m_DiscreteActionStep;
                state.Y = Clamp(state.Y, -1.0f, 1.0f);
                return;
            case DiscreteAction.YMinus:
                state.Y -= m_DiscreteActionStep;
                state.Y = Clamp(state.Y, -1.0f, 1.0f);
                return;
            case DiscreteAction.ZPlus:
                state.Z += m_DiscreteActionStep;
                state.Z = Clamp(state.Z, -1.0f, 1.0f);
                return;
            case DiscreteAction.ZMinus:
                state.Z -= m_DiscreteActionStep;
                state.Z = Clamp(state.Z, -1.0f, 1.0f);
                return;
            case DiscreteAction.ThrottlePlus:
                m_Throttle += m_DiscreteActionStep;
                m_Throttle = Clamp(m_Throttle, -1.0f, 1.0f);
                return;
            case DiscreteAction.ThrottleMinus:
                m_Throttle -= m_DiscreteActionStep;
                m_Throttle = Clamp(m_Throttle, -1.0f, 1.0f);
                return;
            case DiscreteAction.Stage:
                Staging.ActivateNextStage();
                return;
            case DiscreteAction.Gear:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Gear);
                return;
            case DiscreteAction.Light:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Light);
                return;
            case DiscreteAction.RCS:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.RCS);
                return;
            case DiscreteAction.SAS:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.SAS);
                return;
            case DiscreteAction.Brakes:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Brakes);
                return;
            case DiscreteAction.Abort:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Abort);
                return;
            case DiscreteAction.Custom01:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom01);
                return;
            case DiscreteAction.Custom02:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom02);
                return;
            case DiscreteAction.Custom03:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom03);
                return;
            case DiscreteAction.Custom04:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom04);
                return;
            case DiscreteAction.Custom05:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom05);
                return;
            case DiscreteAction.Custom06:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom06);
                return;
            case DiscreteAction.Custom07:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom07);
                return;
            case DiscreteAction.Custom08:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom08);
                return;
            case DiscreteAction.Custom09:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom09);
                return;
            case DiscreteAction.Custom10:
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom10);
                return;
            case DiscreteAction.EVAJetpackActivate:
                if (eva != null)
                {
                    eva.JetpackDeployed = !eva.JetpackDeployed;
                }
                return;
            case DiscreteAction.EVAJetCounterClockwise:
                return;
            case DiscreteAction.EVAJetpackClockwise:
                return;
            case DiscreteAction.EVAJetPitchPlus:
                return;
            case DiscreteAction.EVAJetPitchMinus:
                return;
            case DiscreteAction.EVAJump:
                return;
            case DiscreteAction.EVAReorientAttitude:
                return;
            case DiscreteAction.EVAUseBoard:
                return;
            case DiscreteAction.EVADirectionJump:
                return;
            case DiscreteAction.EVASprint:
                return;
            case DiscreteAction.EVAHeadlamps:
                if (eva != null)
                {
                    eva.lampOn = !eva.lampOn;
                }
                return;
            case DiscreteAction.CutThrottle:
                m_Throttle = 0.0f;
                return;
            case DiscreteAction.FullThrottle:
                m_Throttle = 1.0f;
                return;
            case DiscreteAction.NextPreset:
                if (m_CurrentPreset >= m_Presets.Count - 1)
                {
                    return;
                }

                m_CurrentPreset++;
                return;
            case DiscreteAction.PreviousPreset:
                if (m_CurrentPreset <= 0)   
                {
                    return;
                }

                m_CurrentPreset--;
                return;
            case DiscreteAction.CameraZoomPlus:
                FlightCamera.fetch.SetDistance(FlightCamera.fetch.Distance + m_DiscreteActionStep);
                return; 
            case DiscreteAction.CameraZoomMinus:
                FlightCamera.fetch.SetDistance(FlightCamera.fetch.Distance - m_DiscreteActionStep);
                return;
            case DiscreteAction.CameraXPlus:
                FlightCamera.CamHdg += m_DiscreteActionStep;
                return;
            case DiscreteAction.CameraXMinus:
                FlightCamera.CamHdg -= m_DiscreteActionStep;
                return;
            case DiscreteAction.CameraYPlus:
                FlightCamera.CamPitch += m_DiscreteActionStep;
                return;
            case DiscreteAction.CameraYMinus:
                FlightCamera.CamPitch -= m_DiscreteActionStep;
                return;
            case DiscreteAction.OrbitMapToggle:
                if(!MapView.MapIsEnabled)
                {
                    MapView.EnterMapView();
                }
                else
                {
                    MapView.ExitMapView();
                }
                return;
            case DiscreteAction.TimeWarpPlus:
                TimeWarp.SetRate(TimeWarp.CurrentRateIndex + 1, false);
                return;
            case DiscreteAction.TimeWarpMinus:
                if (TimeWarp.CurrentRateIndex <= 0)
                {
                    break;
                }

                TimeWarp.SetRate(TimeWarp.CurrentRateIndex - 1, false);
                return;
            case DiscreteAction.NavballToggle:
                if (MapView.MapIsEnabled)
                {
                    MapView.fetch.maneuverModeToggle.OnPress.Invoke();
                }

                return;
            case DiscreteAction.Screenshot:
                return;
            case DiscreteAction.QuickSave:
                return;
            case DiscreteAction.IVAViewToggle:
                if (CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.IVA)
                {
                    m_OriginalCameraMode = CameraManager.Instance.currentCameraMode;
                    CameraManager.Instance.SetCameraMode(CameraManager.CameraMode.IVA);
                }
                else
                {
                    CameraManager.Instance.SetCameraMode(CameraManager.CameraMode.IVA);
                }
                return;
            case DiscreteAction.CameraViewToggle:
                FlightCamera.fetch.SetNextMode();
                return;
            case DiscreteAction.SASHold:
                FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                return;
            case DiscreteAction.LockStage:
                return;
            case DiscreteAction.TogglePrecisionControls:
                return;
            case DiscreteAction.ResetTrim:
                state.ResetTrim();
                return;
            }
        }

        private void EvaluateDiscreteActionRelease(DiscreteAction action, FlightCtrlState state)
        {
            switch (action)
            {
            case DiscreteAction.None:
                return;
            case DiscreteAction.SASHold:
                FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                return;
            }
        }

        private void EvaluateContinuousAction(ContinuousAction action, float value, FlightCtrlState state)
        {
            switch (action)
            {
                case ContinuousAction.None:
                    return;
                case ContinuousAction.Yaw:
                    state.yaw = value;
                    state.yaw = Clamp(state.yaw, -1.0f, 1.0f);
                    return;
                case ContinuousAction.YawTrim:
                    state.yawTrim = value;
                    state.yawTrim = Clamp(state.yawTrim, -1.0f, 1.0f);
                    return;
                case ContinuousAction.Pitch:
                    state.pitch = value;
                    state.pitch = Clamp(state.pitch, -1.0f, 1.0f);
                    return;
                case ContinuousAction.PitchTrim:
                    state.pitchTrim = value;
                    state.pitchTrim = Clamp(state.pitchTrim, -1.0f, 1.0f);
                    return;
                case ContinuousAction.Roll:
                    state.roll = value;
                    state.roll = Clamp(state.roll, -1.0f, 1.0f);
                    return;
                case ContinuousAction.RollTrim:
                    state.rollTrim = value;
                    state.rollTrim = Clamp(state.rollTrim, -1.0f, 1.0f);
                    return;
                case ContinuousAction.X:
                    state.X = value;
                    state.X = Clamp(state.X, -1.0f, 1.0f);
                    return;
                case ContinuousAction.Y:
                    state.Y = value;
                    state.Y = Clamp(state.Y, -1.0f, 1.0f);
                    return;
                case ContinuousAction.Z:
                    state.Z = value;
                    state.Z = Clamp(state.Z, -1.0f, 1.0f);
                    return;
                case ContinuousAction.Throttle:
                    m_Throttle += value;
                    m_Throttle = Clamp(m_Throttle, -1.0f, 1.0f);
                    return;
                case ContinuousAction.ThrottleIncrement:
                    m_Throttle += value * m_IncrementalThrottleSensitivity;
                    m_Throttle = Clamp(m_Throttle, -1.0f, 1.0f);
                    return;
                case ContinuousAction.ThrottleDecrement:
                    m_Throttle -= value * m_IncrementalThrottleSensitivity;
                    m_Throttle = Clamp(m_Throttle, -1.0f, 1.0f);
                    return;
                case ContinuousAction.CameraX:
                    FlightCamera.CamHdg += value;
                    break;
                case ContinuousAction.CameraY:
                    FlightCamera.CamPitch += value;
                    break;
                case ContinuousAction.CameraZoom:
                    FlightCamera.fetch.SetDistance(FlightCamera.fetch.Distance + value);
                    break;
            }
        }

        private void FixedUpdate()
        {
            if(HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
            {
                if(!m_CallbackSet)
                {
                    m_Callback = new FlightInputCallback(OnFlyByWire);
                }
                else
                {
                    FlightGlobals.ActiveVessel.OnFlyByWire -= m_Callback;
                }

                FlightGlobals.ActiveVessel.OnFlyByWire += m_Callback;
                m_CallbackSet = true;
            }
        }

        private FlightInputCallback m_Callback;

        void OnGUI()
        {
           // GUI.Window(0, new Rect(32, 32, 400, 600), DoMainWindow, "Advanced FlyByWire");
            PresetEditor.OnGUI();
        }

    }

}