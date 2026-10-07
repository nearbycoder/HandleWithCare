using System.Collections;
using HWC.Sim;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace HWC.Gameplay
{
    /// <summary>The review says what a trip earned: a new star, the total, a tape it unlocked (USE IT).</summary>
    public sealed partial class AutoPilot
    {
        /// <summary>
        /// Progress one star short of the Teal Polka tape (16): deliveries 1–4 with three stars, delivery 5
        /// with two (the budget star missing), delivery 6 with one.
        /// </summary>
        static void SeedOneShortOfPolka(SaveData save)
        {
            save.Records.Clear();
            for (int n = 1; n <= 4; n++)
                save.Records.Add(new SaveData.LevelRecord { Number = n, Stars = 3, Delivered = true, UnderBudget = true, Careful = true, Attempts = 1 });
            save.Records.Add(new SaveData.LevelRecord { Number = 5, Stars = 2, Delivered = true, Careful = true, Attempts = 1 });
            save.Records.Add(new SaveData.LevelRecord { Number = 6, Stars = 1, Delivered = true, Attempts = 1 });
            save.Tape = "kraft";
        }

        /// <summary>tour.sh: the line and the sticker after the trip, T puts the tape on, replay keeps them,
        /// the next box is sealed with it, and a trip with nothing new says nothing about gains.</summary>
        IEnumerator EarnedOnReview()
        {
            var g = Game.I;
            SeedOneShortOfPolka(g.Save);
            Check(g.Save.TotalStars == 15, $"seeded: 15 stars, one short of the Teal Polka tape ({g.Save.TotalStars})");
            yield return RunLevel(5, "ref", false);
            yield return new WaitForSecondsRealtime(1.9f);   // the stars, then the sticker
            string line = g.Hud.ProgressLine;
            Debug.Log("[AutoPilot] review line: " + line);
            Check(line.Contains("+1 STAR: UNDER BUDGET") && line.Contains("16 of 75 stars") && line.Contains("FRAGILE tape at 24"),
                  "the review names the new star, the total and the next tape");
            Check(g.Hud.TapeStickerShowing && g.Hud.TapeUseButton.Label.text == "USE IT", "a NEW TAPE sticker with USE IT");
            Shot("E1_review_new_tape");
            yield return AfterShot();
            yield return Key(UnityEngine.InputSystem.Key.T);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(g.Save.Tape == "polka" && g.Hud.TapeUseButton.Label.text == "ON YOUR BOXES" && !g.Hud.TapeUseButton.Interactable,
                  $"T puts the Teal Polka tape on ('{g.Save.Tape}', button '{g.Hud.TapeUseButton.Label.text}')");
            Shot("E2_tape_in_use");
            yield return AfterShot();
            yield return Key(UnityEngine.InputSystem.Key.P);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 3f);
            yield return new WaitForSecondsRealtime(1.9f);
            Check(g.Phase == Phase.Results && g.Hud.ProgressLine == line && g.Hud.TapeStickerShowing && g.Hud.TapeUseButton.Label.text == "ON YOUR BOXES",
                  "after REPLAY the review shows the same line and the sticker, tape in use");
            // the same packing again: sealed with the new tape, and nothing new earned
            yield return Key(UnityEngine.InputSystem.Key.R);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            yield return WaitPhase(Phase.Journey, 6f);
            Check(g.Station.Box.TapeStyle == "polka", $"the next box is sealed with the Teal Polka tape ('{g.Station.Box.TapeStyle}')");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Reveal, 3f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 3f);
            yield return new WaitForSecondsRealtime(1.9f);
            line = g.Hud.ProgressLine;
            Check(!line.Contains("+") && line.StartsWith("16 of 75 stars") && !g.Hud.TapeStickerShowing, $"a trip with nothing new: '{line}', no sticker");
            Shot("E3_review_nothing_new");
            yield return AfterShot();
        }

        /// <summary>padpilot.sh: the d-pad reaches USE IT on the review, and A presses it.</summary>
        IEnumerator PadUseTape()
        {
            var g = Game.I;
            var keep = new System.Collections.Generic.List<SaveData.LevelRecord>(g.Save.Records);
            string tape = g.Save.Tape;
            SeedOneShortOfPolka(g.Save);
            yield return RunLevel(5, "ref", false);
            yield return new WaitForSecondsRealtime(1.9f);
            PadCheck(g.Hud.TapeStickerShowing, "the review shows the NEW TAPE sticker");
            yield return PadOnto(g.Hud.TapeUseButton.Image.rectTransform);
            yield return PadButton(GamepadButton.South);
            yield return new WaitForSecondsRealtime(0.3f);
            PadCheck(g.Save.Tape == "polka" && g.Hud.TapeUseButton.Label.text == "ON YOUR BOXES", $"d-pad onto USE IT and A: the Teal Polka tape is on ('{g.Save.Tape}')");
            Shot("E4_pad_use_tape");
            yield return AfterShot();
            g.Save.Records.Clear(); g.Save.Records.AddRange(keep); g.Save.Tape = tape;   // as the later steps expect
            yield return PadButton(GamepadButton.West);   // X: back to the bench, where the next step starts
            yield return new WaitForSecondsRealtime(0.6f);
            PadCheck(g.Phase == Phase.Packing, "X repacks from the review");
        }
    }
}
