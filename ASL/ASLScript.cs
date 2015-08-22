﻿using LiveSplit.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;

namespace LiveSplit.ASL
{
    public class ASLScript
    {
        protected TimerModel Model { get; set; }
        protected Process Game { get; set; }
        public ASLState OldState { get; set; }
        public ASLState State { get; set; }
        public Dictionary<string, List<ASLState>> States { get; set; } // TODO: don't use dict
        public ExpandoObject Vars { get; set; }
        public string Version { get; set; }
        public ASLMethod Init { get; set; }
        public ASLMethod Update { get; set; }
        public ASLMethod Start { get; set; }
        public ASLMethod Split { get; set; }
        public ASLMethod Reset { get; set; }
        public ASLMethod IsLoading { get; set; }
        public ASLMethod GameTime { get; set; }

        public bool UsesGameTime { get; private set; }

        public ASLScript(
            Dictionary<string, List<ASLState>> states,
            ASLMethod init, ASLMethod update,
            ASLMethod start, ASLMethod reset,
            ASLMethod split, 
            ASLMethod isLoading, ASLMethod gameTime)
        {
            States = states;
            Vars = new ExpandoObject();
            Init = init ?? new ASLMethod("");
            Update = update ?? new ASLMethod("");
            Start = start ?? new ASLMethod("");
            Split = split ?? new ASLMethod("");
            Reset = reset ?? new ASLMethod("");
            IsLoading = isLoading ?? new ASLMethod("");
            GameTime = gameTime ?? new ASLMethod("");
            UsesGameTime = IsLoading.IsEmpty || GameTime.IsEmpty;
            Version = String.Empty;
        }

        protected void TryConnect(LiveSplitState lsState)
        {
            if (Game == null || Game.HasExited)
            {
                Game = null;

                var stateProcess = States.Keys.Select(proccessName => new
                {
                    // default to first defined state in file (lazy)
                    // TODO: default to the one with no version specified, if it exists
                    State = States[proccessName].First(),
                    Process = Process.GetProcessesByName(proccessName).FirstOrDefault()
                }).FirstOrDefault(x => x.Process != null);

                if (stateProcess != null)
                {
                    Game = stateProcess.Process;
                    State = stateProcess.State;
                    State.RefreshValues(Game);
                    OldState = State;
                    Version = String.Empty;

                    string ver = Version;
                    Init.Run(lsState, OldState, State, Vars, Game, ref ver);
                    if (ver != Version)
                    {
                        var state =
                            States.Where(kv => kv.Key.ToLower() == Game.ProcessName.ToLower())
                                .Select(kv => kv.Value)
                                .First() // states
                                .FirstOrDefault(s => s.GameVersion == ver);
                        if (state != null)
                        {
                            State = state;
                            State.RefreshValues(Game);
                            OldState = State;
                            Version = ver;
                        }
                    }
                }
            }
        }

        public void RunUpdate(LiveSplitState lsState)
        {
            if (Game != null && !Game.HasExited)
            {
                OldState = State.RefreshValues(Game);

                string ver = Version;
                Update.Run(lsState, OldState, State, Vars, Game, ref ver);

                if (lsState.CurrentPhase == TimerPhase.Running || lsState.CurrentPhase == TimerPhase.Paused)
                {
                    var isPaused = IsLoading.Run(lsState, OldState, State, Vars, Game, ref ver);
                    if (isPaused != null)
                        lsState.IsGameTimePaused = isPaused;

                    var gameTime = GameTime.Run(lsState, OldState, State, Vars, Game, ref ver);
                    if (gameTime != null)
                        lsState.SetGameTime(gameTime);

                    if (Reset.Run(lsState, OldState, State, Vars, Game, ref ver) ?? false)
                    {
                        Model.Reset();
                    }
                    else if (Split.Run(lsState, OldState, State, Vars, Game, ref ver) ?? false)
                    {
                        Model.Split();
                    }
                }

                if (lsState.CurrentPhase == TimerPhase.NotRunning)
                {
                    if (Start.Run(lsState, OldState, State, Vars, Game, ref ver) ?? false)
                    {
                        Model.Start();

                        if (UsesGameTime)
                            Model.InitializeGameTime();
                    }
                }
            }
            else
            {
                if (Model == null)
                {
                    Model = new TimerModel() { CurrentState = lsState };
                }
                TryConnect(lsState);
            }
        }
    }
}
