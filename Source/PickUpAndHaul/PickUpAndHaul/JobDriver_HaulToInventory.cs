using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using System.Diagnostics;
using UnityEngine;

namespace PickUpAndHaul
{
    public class JobDriver_HaulToInventory : JobDriver
    {

        CombatExtended.CompInventory ceCompInventory = null;

        public override bool TryMakePreToilReservations()
        {
            return this.pawn.Reserve(this.job.targetA, this.job, 1, -1, null);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThings = takenToInventory.GetHashSet();
            //this.FailOnDestroyedOrNull(TargetIndex.A);
            
            if (ModCompatibilityCheck.CombatExtendedIsActive)
            {
                ceCompInventory = new CombatExtended.CompInventory();
                ceCompInventory.parent = pawn;
            }

            Toil wait = Toils_General.Wait(2);
            Toil reserveTargetA = Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null);
            yield return reserveTargetA;

            Toil gotoThing = new Toil
            {
                initAction = () =>
                {
                    this.pawn.pather.StartPath(this.TargetThingA, PathEndMode.ClosestTouch);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return gotoThing;
            
            Toil takeThing = new Toil
            {
                initAction = () =>
                {
                    Pawn actor = this.pawn;
                    Thing thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
                    Toils_Haul.ErrorCheckForCarry(actor, thing);

                    int num = 0;

                    if (ModCompatibilityCheck.CombatExtendedIsActive)
                    {
                        ceCompInventory.CanFitInInventory(thing, out int count, false, true);
                        num = count;
                        Log.Message(num.ToString());
                    }
                    else
                    {
                        num = Mathf.Min(thing.stackCount, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));
                    }
                    if (num <= 0)
                    {
                        Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);

                        if (haul?.TryMakePreToilReservations(actor) ?? false)
                        {
                            actor.jobs.jobQueue.EnqueueFirst(haul, new JobTag?(JobTag.Misc));
                        }
                        actor.jobs.curDriver.JumpToToil(wait);
                    }
                    else
                    {
                        //Merging and unmerging messes up the picked up ID (which already gets messed up enough)
                        actor.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(num), true); 
                        takenToInventory.RegisterHauledItem(thing);
                        if (ModCompatibilityCheck.CombatExtendedIsActive)
                        {
                            ceCompInventory.UpdateInventory();
                        }
                    }
                }
            };
            yield return takeThing;
            yield return CheckDuplicateItemsToHaulToInventory(reserveTargetA, TargetIndex.A);
            yield return wait;
        }
        

        //regular Toils_Haul.CheckForGetOpportunityDuplicate isn't going to work for our purposes, since we're not carrying anything. 
        //Carrying something yields weird results with unspawning errors when transfering to inventory, so we copy-past-- I mean, implement our own.
        public Toil CheckDuplicateItemsToHaulToInventory(Toil getHaulTargetToil, TargetIndex haulableInd, Predicate<Thing> extraValidator = null)
        {
            Toil toil = new Toil();
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;

                Predicate<Thing> validator = (Thing t) => t.Spawned
                    && HaulAIUtility.PawnCanAutomaticallyHaulFast(actor, t, false)
                    && (!t.IsInValidBestStorage())
                    && !t.IsForbidden(actor)
                    && !(t is Corpse)
                    && actor.CanReserve(t, 1, -1, null, false);
                  //  && (extraValidator == null ||  extraValidator(t));

                Thing thing = GenClosest.ClosestThingReachable(actor.Position, actor.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways), PathEndMode.ClosestTouch, 
                    TraverseParms.For(actor, Danger.Deadly, TraverseMode.ByPawn, false), 12f, validator, null, 0, -1, false, RegionType.Set_Passable, false);

                float availableBulk = 1f;
                float availableWeight = 1f;

                if (ModCompatibilityCheck.CombatExtendedIsActive)
                {
                    availableWeight = ceCompInventory.currentWeight / ceCompInventory.capacityWeight;
                    availableBulk = ceCompInventory.currentBulk / ceCompInventory.capacityBulk;
                }
                Log.Message(availableBulk.ToString());
                Log.Message(availableWeight.ToString());

                if (thing != null && (MassUtility.EncumbrancePercent(actor) <= 0.9f || availableBulk <= 0.7f || availableWeight <= 0.8f))
                {
                    curJob.SetTarget(haulableInd, thing);
                    actor.jobs.curDriver.JumpToToil(getHaulTargetToil);
                    return;
                }
                if (thing != null)
                {
                    Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                    if (haul?.TryMakePreToilReservations(actor) ?? false)
                    {
                        actor.jobs.jobQueue.EnqueueFirst(haul, new JobTag?(JobTag.Misc));
                        return;
                    }
                }
                if (thing == null)
                {
                    Job job = new Job(PickUpAndHaulJobDefOf.UnloadYourHauledInventory);
                    if (job.TryMakePreToilReservations(actor))
                    {
                        actor.jobs.jobQueue.EnqueueFirst(job, new JobTag?(JobTag.Misc));
                        return;
                    }
                }
            };
            return toil;
        }
    }
}