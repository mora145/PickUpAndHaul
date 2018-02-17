using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace PickUpAndHaul
{
    public class JobDriver_HaulToInventory : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            return this.pawn.Reserve(job.targetA, this.job, 1, -1, null);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThings = takenToInventory.GetHashSet();

            //this.FailOnDestroyedOrNull(TargetIndex.A);


            Toil wait = Toils_General.Wait(2);
            Toil reserveTargetA = Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null);
            Toil checkDuplicateItemsToHaulToInventory = CheckDuplicateItemsToHaulToInventory(reserveTargetA, TargetIndex.A, null);

            Toil haulUrgentlyCheck = new Toil
            {
                initAction = () =>
                {
                    if (ModCompatibilityCheck.AllowToolIsActive)
                    {
                        DesignationDef HaulUrgentlyDesignation = DefDatabase<DesignationDef>.GetNamed("HaulUrgentlyDesignation", true);
                        if (pawn.Map.designationManager.DesignationOn(pawn.CurJob.targetA.Thing)?.def == HaulUrgentlyDesignation)
                        {
                            //TODO: use jumptoil instead.
                            checkDuplicateItemsToHaulToInventory = CheckDuplicateItemsToHaulToInventory(reserveTargetA, TargetIndex.A, (Thing x) => pawn.Map.designationManager.DesignationOn(x)?.def == HaulUrgentlyDesignation);
                        }
                    }
                }
            };
            yield return haulUrgentlyCheck;


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

                    int num = Mathf.Min(thing.stackCount, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));

                    // yo dawg, I heard you like delegates so I put delegates in your delegate, so you can delegate your delegates.
                    // because compilers don't respect IF statements in delegates and toils are fully iterated over as soon as the job starts.
                    try
                    {
                        ((Action)(() =>
                        {
                            if (ModCompatibilityCheck.CombatExtendedIsActive)
                            {
                                CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                                ceCompInventory.CanFitInInventory(thing, out int count, false, false);
                                num = count;
                            }
                        }))();
                    }
                    catch (TypeLoadException) { }


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
                        try
                        {
                            ((Action)(() =>
                            {
                                if (ModCompatibilityCheck.CombatExtendedIsActive)
                                {
                                    CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                                    ceCompInventory.UpdateInventory();
                                }
                            }))();
                        }
                        catch (TypeLoadException) { }
                        
                    }
                }
            };
            yield return takeThing;
            yield return checkDuplicateItemsToHaulToInventory;
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
                    && (extraValidator == null || extraValidator (t))
                    && actor.CanReserve(t, 1, -1, null, false);

                Log.Message(extraValidator.ToStringSafe());

                Thing thing = GenClosest.ClosestThingReachable(actor.Position, actor.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways), PathEndMode.ClosestTouch, 
                    TraverseParms.For(actor, Danger.Deadly, TraverseMode.ByPawn, false), 12f, validator, null, 0, -1, false, RegionType.Set_Passable, false);

                float usedBulkByPct = 0f;
                float usedWeightByPct = 0f;

                try
                {
                    ((Action)(() =>
                    {
                        if (ModCompatibilityCheck.CombatExtendedIsActive)
                        {
                            CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                            usedWeightByPct = ceCompInventory.currentWeight / ceCompInventory.capacityWeight;
                            usedBulkByPct = ceCompInventory.currentBulk / ceCompInventory.capacityBulk;
                        }
                    }))();
                }
                catch (TypeLoadException) { }


                if (thing != null && (MassUtility.EncumbrancePercent(actor) <= 0.9f || usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f))
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