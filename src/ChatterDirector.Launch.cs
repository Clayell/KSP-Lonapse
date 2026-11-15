// Flight moments with their own exchange: Mission Control and the crew on the launch pad (launch_pad), the call
// right after liftoff (liftoff), a stable orbit (orbit_reached), the burning stage running low on fuel (fuel_low),
// the first heat of an atmospheric entry before the blackout (reentry_heat) and the vessel at rest on the home body
// after a trip to space (welcome_home).
// Each plays once per vessel (orbit once per body, fuel once per stage, again after a refuel) and waits for a free radio. Without a fitting
// scene nothing plays. Crew only: probe mode has its own milestones.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        /// <summary>Seconds on the pad before the pad exchange: room for the scene to settle and the player to look
        /// around.</summary>
        const float PadDelayMin = 6f, PadDelayMax = 12f;
        /// <summary>Seconds after leaving the pad before the liftoff exchange, so it does not land on the staging
        /// callout. After LiftoffMaxWait s without a free radio it is dropped.</summary>
        const float LiftoffDelayMin = 1.5f, LiftoffDelayMax = 3f, LiftoffMaxWait = 15f;
        /// <summary>Entry heat: the call starts at the entry interface (PacingRules.EntryInterface, at least
        /// EntrySpeedShare of the orbit speed) or, as a fallback, at HeatNoticePlasma (the blackout starts at
        /// Tuning.blackoutEnter, 0.35 by default). Either must hold HeatNoticeTime s. A new entry may get its own call
        /// after HeatResetTime s above the atmosphere (an aerobrake pass skipping out).</summary>
        const float HeatNoticePlasma = 0.06f, HeatNoticeTime = 0.75f, HeatResetTime = 5f, EntrySpeedShare = 0.6f;
        /// <summary>Orbit: seconds after KSP says ORBITING before the call, and how long it may wait for the radio or
        /// for the periapsis to clear the lowest safe orbit.</summary>
        const float OrbitDelayMin = 1.5f, OrbitDelayMax = 3.5f, OrbitMaxWait = 30f;
        /// <summary>Low fuel: the percent of the burning stage that gets the call, the percent above which the stage
        /// may call again (refuelled), how often the tanks are counted and how long the call may wait.</summary>
        const float FuelLowPercent = 10f, FuelResetPercent = 25f, FuelCheckEvery = 1f, FuelMaxWait = 8f;
        /// <summary>Welcome home: slower than HomeRestSpeed m/s for HomeRestTime s on the home body. After
        /// HomeMaxWait s at rest without a free radio it is dropped.</summary>
        const float HomeRestSpeed = 1f, HomeRestTime = 4f, HomeMaxWait = 120f;

        Guid launchVessel;
        bool padDone, liftoffDone, heatDone;
        /// <summary>Vessels that had their pad or liftoff exchange this scene: an EVA and back does not repeat it.</summary>
        readonly HashSet<Guid> padSaid = new HashSet<Guid>(), liftoffSaid = new HashSet<Guid>();
        /// <summary>Vessels that were in space or at another body, and the ones that were welcomed home.</summary>
        readonly HashSet<Guid> spaceSeen = new HashSet<Guid>(), homeSaid = new HashSet<Guid>();
        /// <summary>Orbits called (vessel id and body) and low fuel calls (vessel id and stage).</summary>
        readonly HashSet<string> orbitSaid = new HashSet<string>(), fuelSaid = new HashSet<string>();
        float padAt = -1f, liftoffAt = -1f, heatTimer, heatQuiet;
        float orbitAt = -1f, fuelCheckAt, fuelAt = -1f, homeRest;

        /// <summary>From the addon: the active vessel left the launch pad (PRELAUNCH to FLYING).</summary>
        public void OnLiftoff()
        {
            Vessel v = ActiveVessel;
            if (v == null || host.probeMode || v.isEVA) return;
            LaunchVesselCheck(v);
            if (liftoffDone) return;
            padDone = true;   // too late for the pad call
            padSaid.Add(v.id);
            liftoffAt = clock + U(LiftoffDelayMin, LiftoffDelayMax);
            Log.Info("Liftoff: exchange due in " + (liftoffAt - clock).ToString("0.0") + " s");
        }

        /// <summary>From the addon: the active vessel went from FLYING or SUB_ORBITAL to ORBITING. Once per vessel and
        /// body. A capture from an escape orbit is not counted.</summary>
        public void OnOrbitReached()
        {
            Vessel v = ActiveVessel;
            if (v == null || host.probeMode || v.isEVA || v.mainBody == null) return;
            LaunchVesselCheck(v);
            string key = v.id + ":" + v.mainBody.bodyName;
            if (!orbitSaid.Add(key)) return;
            orbitAt = clock + U(OrbitDelayMin, OrbitDelayMax);
            Log.Info("Orbit around " + v.mainBody.bodyName + ": exchange due in " + (orbitAt - clock).ToString("0.0") + " s");
        }

        void LaunchVesselCheck(Vessel v)
        {
            if (v.id == launchVessel) return;
            launchVessel = v.id;
            padDone = padSaid.Contains(v.id);
            liftoffDone = liftoffSaid.Contains(v.id);
            heatDone = false;
            padAt = clock + U(PadDelayMin, PadDelayMax);
            liftoffAt = orbitAt = fuelAt = -1f;
            heatTimer = heatQuiet = homeRest = 0f;
        }

        /// <summary>The radio is free for a launch or entry exchange: no call, chat or event is waiting.</summary>
        bool LaunchAirFree
        {
            get
            {
                return host.CanStartAudio && host.settings.radioEnabled && !radioSilent && !McUnreachable && !Somber
                       && count == 0 && Idle && !Holding && !calloutPending && !ChatHoldsAir;
            }
        }

        /// <summary>From Update, once per frame outside a mod, docking or science hold.</summary>
        void LaunchTick(float dt)
        {
            Vessel v = ActiveVessel;
            if (v == null || host.probeMode || v.isEVA) return;
            LaunchVesselCheck(v);
            Vessel.Situations sit;
            try { sit = v.situation; } catch (Exception) { return; }
            bool onPad = sit == Vessel.Situations.PRELAUNCH;

            // on the pad: once, after the scene settled, not while the countdown runs
            if (!padDone && onPad && clock >= padAt && (host.countdown == null || !host.countdown.Running) && LaunchAirFree)
            {
                padDone = true;
                padSaid.Add(v.id);
                PlayLaunchScene("launch_pad", U(0f, 0.4f));
            }

            // after liftoff: once, when the radio is free, dropped after LiftoffMaxWait
            if (!liftoffDone && liftoffAt >= 0f && clock >= liftoffAt)
            {
                if (clock - liftoffAt > LiftoffMaxWait) { liftoffDone = true; Log.Info("Liftoff exchange dropped: no free radio"); }
                else if (LaunchAirFree) { liftoffDone = true; PlayLaunchScene("liftoff", 0f); }
                if (liftoffDone) liftoffSaid.Add(v.id);
            }

            OrbitTick(v, sit);
            FuelTick(v, onPad);
            HomeTick(v, sit, dt);

            // entry heat: once per entry, at the entry interface or when the plasma starts, before the blackout
            float p = host.stress.plasma;
            CelestialBody b = v.mainBody;
            bool inAir = b != null && b.atmosphere && v.altitude < b.atmosphereDepth;
            if (!inAir) { heatQuiet += dt; if (heatQuiet >= HeatResetTime) heatDone = false; }
            else heatQuiet = 0f;
            bool entering = false;
            try
            {
                if (inAir)
                {
                    double r = b.Radius + v.altitude;
                    double circular = r > 0 ? Math.Sqrt(b.gravParameter / r) : double.NaN;
                    entering = PacingRules.EntryInterface(v.altitude, b.atmosphereDepth, v.verticalSpeed, v.srfSpeed, circular, EntrySpeedShare);
                }
            }
            catch (Exception) { entering = false; }
            bool clear = host.blackout == null || host.blackout.state == BlackoutState.Clear;
            bool heating = p >= HeatNoticePlasma && CurrentPhase == FlightPhase.Reentry;
            if (!heatDone && clear && (entering || heating)) heatTimer += dt;
            else heatTimer = 0f;
            if (heatTimer >= HeatNoticeTime && LaunchAirFree)
            {
                heatDone = true;
                heatTimer = 0f;
                PlayLaunchScene("reentry_heat", 0f);
            }
        }

        /// <summary>The orbit call once its delay ran: still in orbit and the periapsis clear of the atmosphere or the
        /// mountains. Waits up to OrbitMaxWait for both and a free radio.</summary>
        void OrbitTick(Vessel v, Vessel.Situations sit)
        {
            if (orbitAt < 0f || clock < orbitAt) return;
            if (sit != Vessel.Situations.ORBITING) { orbitAt = -1f; Log.Info("Orbit exchange dropped: no longer in orbit"); return; }
            if (clock - orbitAt > OrbitMaxWait) { orbitAt = -1f; Log.Info("Orbit exchange dropped: no stable orbit or no free radio"); return; }
            bool stable;
            try { stable = PacingRules.StableOrbit(v.orbit.eccentricity, v.orbit.PeR, v.mainBody.minOrbitalDistance); }
            catch (Exception) { stable = false; }
            if (!stable || !LaunchAirFree) return;
            orbitAt = -1f;
            PlayLaunchScene("orbit_reached", 0f);
        }

        /// <summary>Low fuel in the burning stage when no engine waits in a later stage: once per stage, again after a
        /// refuel above FuelResetPercent.</summary>
        void FuelTick(Vessel v, bool onPad)
        {
            if (onPad || clock < fuelCheckAt) return;
            fuelCheckAt = clock + FuelCheckEvery;
            string key = v.id + ":" + v.currentStage;
            if (fuelAt >= 0f)
            {
                if (clock - fuelAt > FuelMaxWait) { fuelAt = -1f; Log.Info("Low fuel exchange dropped: no free radio"); }
                else if (LaunchAirFree) { fuelAt = -1f; PlayLaunchScene("fuel_low", 0f); }
                return;
            }
            double throttle;
            try { throttle = v.ctrlState != null ? v.ctrlState.mainThrottle : 0f; } catch (Exception) { return; }
            if (throttle <= 0.05) return;   // counting the tanks walks every part: only while an engine may burn
            // every stage runs dry on a normal launch: only the last engine stage gets the call
            if (LaterEngineStage(v)) return;
            double pct = StageFuelPercent(v, false, true);
            if (!double.IsNaN(pct) && pct > FuelResetPercent) { fuelSaid.Remove(key); return; }
            if (!PacingRules.FuelLow(pct, throttle, FuelLowPercent) || !fuelSaid.Add(key)) return;
            fuelAt = clock;
            Log.Info("Low fuel: " + pct.ToString("0.0") + "% in the burning stage " + v.currentStage);
        }

        /// <summary>An engine waits in a later stage (not ignited, not flamed out): the fuel of this stage is not the
        /// last.</summary>
        static bool LaterEngineStage(Vessel v)
        {
            try
            {
                foreach (Part p in v.parts)
                {
                    if (p.inverseStage >= v.currentStage) continue;
                    foreach (PartModule m in p.Modules)
                    {
                        var e = m as ModuleEngines;
                        if (e != null && !e.EngineIgnited && !e.flameout) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Welcome home: remembers a trip to space, then waits for the vessel to rest on the home body.</summary>
        void HomeTick(Vessel v, Vessel.Situations sit, float dt)
        {
            CelestialBody home, body = v.mainBody;
            try { home = FlightGlobals.GetHomeBody(); } catch (Exception) { return; }
            if (body == null || home == null) return;
            bool atHome = ReferenceEquals(body, home);
            if (!atHome || sit == Vessel.Situations.ORBITING || sit == Vessel.Situations.ESCAPING
                || (body.atmosphere && v.altitude > body.atmosphereDepth))
                spaceSeen.Add(v.id);
            if (homeSaid.Contains(v.id)) return;
            bool down = sit == Vessel.Situations.LANDED || sit == Vessel.Situations.SPLASHED;
            double speed;
            try { speed = v.srfSpeed; } catch (Exception) { speed = double.NaN; }
            if (!PacingRules.HomeRest(spaceSeen.Contains(v.id), atHome, down, speed, HomeRestSpeed)) { homeRest = 0f; return; }
            homeRest += dt;
            if (homeRest < HomeRestTime) return;
            if (homeRest > HomeRestTime + HomeMaxWait) { homeSaid.Add(v.id); Log.Info("Welcome home dropped: no free radio"); return; }
            if (!LaunchAirFree) return;
            homeSaid.Add(v.id);
            PlayLaunchScene("welcome_home", U(0.5f, 1.5f));
        }

        void PlayLaunchScene(string ev, float delay)
        {
            PhraseScene sc = PickScene(ev, null, null);
            bool ok = EnqueueRadioScene(sc, null, delay);
            Log.Info("Exchange " + ev + (ok ? ": " + sc.Id + " queued" : ": no fitting scene"));
        }
    }
}
