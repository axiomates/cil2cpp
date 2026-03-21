using System;
using Stateless;

enum PhoneState { OffHook, Ringing, Connected, OnHold }
enum Trigger { CallDialed, HungUp, CallConnected, PlacedOnHold, TakenOffHold }

class Program
{
    static void Main()
    {
        // [1] Create state machine
        try
        {
            var sm = new StateMachine<PhoneState, Trigger>(PhoneState.OffHook);
            sm.Configure(PhoneState.OffHook)
                .Permit(Trigger.CallDialed, PhoneState.Ringing);
            sm.Configure(PhoneState.Ringing)
                .Permit(Trigger.CallConnected, PhoneState.Connected)
                .Permit(Trigger.HungUp, PhoneState.OffHook);
            sm.Configure(PhoneState.Connected)
                .Permit(Trigger.HungUp, PhoneState.OffHook)
                .Permit(Trigger.PlacedOnHold, PhoneState.OnHold);
            sm.Configure(PhoneState.OnHold)
                .Permit(Trigger.TakenOffHold, PhoneState.Connected)
                .Permit(Trigger.HungUp, PhoneState.OffHook);

            Console.WriteLine($"[1] Initial state: {sm.State}");
        }
        catch (Exception ex) { Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [2] Fire transitions
        try
        {
            var sm = new StateMachine<PhoneState, Trigger>(PhoneState.OffHook);
            sm.Configure(PhoneState.OffHook).Permit(Trigger.CallDialed, PhoneState.Ringing);
            sm.Configure(PhoneState.Ringing).Permit(Trigger.CallConnected, PhoneState.Connected);
            sm.Configure(PhoneState.Connected).Permit(Trigger.HungUp, PhoneState.OffHook);

            sm.Fire(Trigger.CallDialed);
            Console.WriteLine($"[2] After dial: {sm.State}");
            sm.Fire(Trigger.CallConnected);
            Console.WriteLine($"[2] After connect: {sm.State}");
            sm.Fire(Trigger.HungUp);
            Console.WriteLine($"[2] After hangup: {sm.State}");
        }
        catch (Exception ex) { Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [3] IsInState check
        try
        {
            var sm = new StateMachine<PhoneState, Trigger>(PhoneState.Connected);
            sm.Configure(PhoneState.Connected).Permit(Trigger.HungUp, PhoneState.OffHook);
            Console.WriteLine($"[3] IsInState Connected: {sm.IsInState(PhoneState.Connected)}");
            Console.WriteLine($"[3] IsInState OffHook: {sm.IsInState(PhoneState.OffHook)}");
        }
        catch (Exception ex) { Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [4] CanFire check
        try
        {
            var sm = new StateMachine<PhoneState, Trigger>(PhoneState.OffHook);
            sm.Configure(PhoneState.OffHook).Permit(Trigger.CallDialed, PhoneState.Ringing);
            Console.WriteLine($"[4] CanFire CallDialed: {sm.CanFire(Trigger.CallDialed)}");
            Console.WriteLine($"[4] CanFire HungUp: {sm.CanFire(Trigger.HungUp)}");
        }
        catch (Exception ex) { Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [5] Permitted triggers
        try
        {
            var sm = new StateMachine<PhoneState, Trigger>(PhoneState.OffHook);
            sm.Configure(PhoneState.OffHook)
                .Permit(Trigger.CallDialed, PhoneState.Ringing);
            var triggers = sm.GetPermittedTriggers();
            Console.WriteLine($"[5] Permitted triggers: {string.Join(", ", triggers)}");
        }
        catch (Exception ex) { Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [6] OnEntry/OnExit actions
        try
        {
            var log = new System.Collections.Generic.List<string>();
            var sm = new StateMachine<PhoneState, Trigger>(PhoneState.OffHook);
            sm.Configure(PhoneState.OffHook)
                .Permit(Trigger.CallDialed, PhoneState.Ringing)
                .OnExit(() => log.Add("exit-offhook"));
            sm.Configure(PhoneState.Ringing)
                .OnEntry(() => log.Add("enter-ringing"));
            sm.Fire(Trigger.CallDialed);
            Console.WriteLine($"[6] Actions: {string.Join(", ", log)}");
        }
        catch (Exception ex) { Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}"); }
    }
}
