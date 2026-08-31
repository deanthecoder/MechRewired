// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Resources;
using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>Exercises high-value mission invariants against private original data when available.</summary>
[TestFixture]
public sealed class OriginalArchiveIntegrationTests
{
    private const string ArchiveEnvironmentVariable = "MECHREWIRED_MW2_PRJ";

    [TestCase("BWD/PINKSCN1.BWD", "pinklve1", 2000, "pinklve2", 2500)]
    [TestCase("BWD/YELLSCN1.BWD", "yelllve1", 1720, "yelllve2", 1970)]
    public void FirstMissionsRetainAuthoredLeaveAreaWarningAndFailure(
        string scenarioPath,
        string innerBoundaryName,
        int innerRadius,
        string outerBoundaryName,
        int outerRadius)
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, scenarioPath);
        var definition = MechRewired.Missions.MissionDefinition.FromMissionTable(
            mission.Scenario.MissionTables.Single(table => table.Index == 0));
        var boundaries = mission.MissionAreaBoundaries.ToDictionary(
            resource => Path.GetFileNameWithoutExtension(resource.Entry.Name),
            StringComparer.OrdinalIgnoreCase);
        var innerPoint = MechWarriorWorldFile.Load(
            archive.ReadEntry(boundaries[innerBoundaryName].Entry),
            boundaries[innerBoundaryName].Include.Transform).NavPoints.Single();
        var outerPoint = MechWarriorWorldFile.Load(
            archive.ReadEntry(boundaries[outerBoundaryName].Entry),
            boundaries[outerBoundaryName].Include.Transform).NavPoints.Single();
        var boundaryReports = definition.EventReports
            .Where(report =>
                report.Trigger.Kind == MechRewired.Missions.MissionEventKind.MissionAreaBoundaryExited)
            .ToDictionary(
                report => report.Trigger.TargetResourceName,
                report => report.Report.Name,
                StringComparer.OrdinalIgnoreCase);
        var runtime = new MechRewired.Missions.MissionRuntime(definition);
        runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.MissionAreaBoundaryExited,
            innerBoundaryName));
        var outcomeAfterWarning = runtime.Outcome;
        runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.MissionAreaBoundaryExited,
            outerBoundaryName));

        Assert.Multiple(() =>
        {
            Assert.That(mission.MissionAreaBoundaries, Has.Count.EqualTo(2));
            Assert.That(innerPoint.Radius, Is.EqualTo(innerRadius));
            Assert.That(outerPoint.Radius, Is.EqualTo(outerRadius));
            Assert.That(innerPoint.Position, Is.EqualTo(outerPoint.Position));
            Assert.That(boundaryReports[innerBoundaryName], Is.EqualTo("gene018S"));
            Assert.That(boundaryReports[outerBoundaryName], Is.EqualTo("gene019S"));
            Assert.That(definition.FailureEvents.Any(failureEvent =>
                failureEvent.Kind == MechRewired.Missions.MissionEventKind.MissionAreaBoundaryExited &&
                failureEvent.TargetResourceName.Equals(
                    outerBoundaryName,
                    StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(outcomeAfterWarning, Is.EqualTo(MechRewired.Missions.MissionOutcome.Active));
            Assert.That(runtime.Outcome, Is.EqualTo(MechRewired.Missions.MissionOutcome.Failed));
        });
    }

    [Test]
    public void SilentThunderMissionTableRetainsAuthoredControlSemantics()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/PINKSCN1.BWD");
        var table = mission.Scenario.MissionTables.Single(candidate => candidate.Index == 0);
        var showExtraction = table.Entries[12];
        var secondary = table.Entries[14];

        Assert.Multiple(() =>
        {
            Assert.That(table.MissionTimeSeconds, Is.EqualTo(1500));
            Assert.That(showExtraction.Action, Is.EqualTo(MechWarriorMissionAction.None));
            Assert.That(showExtraction.ControlAction,
                Is.EqualTo(MechWarriorMissionControlAction.ShowObjective));
            Assert.That(showExtraction.ConditionLogic, Is.EqualTo(MechWarriorMissionConditionLogic.All));
            Assert.That(showExtraction.ActivationConditions, Is.EqualTo(new[]
            {
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Succeeded, 1, 0),
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Succeeded, 2, 0)
            }));
            Assert.That(showExtraction.TargetObjectiveIndex, Is.EqualTo(20));
            Assert.That(secondary.Action, Is.EqualTo(MechWarriorMissionAction.Wait));
            Assert.That(secondary.GoalFlags, Is.EqualTo(0x02));
            Assert.That(secondary.ActivationConditions.Select(condition => condition.ObjectiveIndex),
                Is.EqualTo(new byte[] { 5, 6, 26 }));
        });
    }

    [Test]
    public void SilentThunderExposesAuthoredOptionalDestructionGoals()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/PINKSCN1.BWD");
        var definition = MechRewired.Missions.MissionDefinition.FromMissionTable(
            mission.Scenario.MissionTables.Single(table => table.Index == 0));
        var secondary = definition.Objectives.Single(objective =>
            objective.Description == "Destroy all Enemy Mechs");
        var tertiary = definition.Objectives.Single(objective =>
            objective.Description == "Destroy Targets of Opportunity");
        var extraction = definition.Objectives.Single(objective =>
            objective.Kind == MechRewired.Missions.MissionObjectiveKind.Extract);
        var navigationReports = definition.EventReports
            .Where(report => report.Trigger.Kind == MechRewired.Missions.MissionEventKind.NavigationPointReached)
            .ToDictionary(
                report => report.Trigger.TargetResourceName,
                report => report.Report.Name,
                StringComparer.OrdinalIgnoreCase);
        var runtime = new MechRewired.Missions.MissionRuntime(definition);
        runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.TargetDestroyed, "pinkENS1"));
        runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.TargetDestroyed, "pinkENS2"));
        var secondaryTransitions = runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.TargetDestroyed, "pinkENS3"));
        runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.TargetDestroyed, "pinkare3"));
        var tertiaryTransitions = runtime.Apply(new MechRewired.Missions.MissionEvent(
            MechRewired.Missions.MissionEventKind.TargetDestroyed, "pinkare5"));

        Assert.Multiple(() =>
        {
            Assert.That(definition.Objectives, Has.Count.EqualTo(5));
            Assert.That(definition.TimeLimitSeconds, Is.EqualTo(1500));
            Assert.That(secondary.IsOptional, Is.True);
            Assert.That(secondary.Kind, Is.EqualTo(MechRewired.Missions.MissionObjectiveKind.Aggregate));
            Assert.That(secondary.SuccessReport.Name, Is.EqualTo("gene002S"));
            Assert.That(secondary.AggregateRequirements.Select(requirement => requirement.TargetResourceName),
                Is.EquivalentTo(new[] { "pinkENS1", "pinkENS2", "pinkENS3" }));
            Assert.That(tertiary.IsOptional, Is.True);
            Assert.That(tertiary.SuccessReport.Name, Is.EqualTo("gene003S"));
            Assert.That(tertiary.AggregateRequirements.Select(requirement => requirement.TargetResourceName),
                Is.EquivalentTo(new[] { "pinkare3", "pinkare5" }));
            Assert.That(secondaryTransitions.Select(transition => transition.Objective.Id),
                Does.Contain(secondary.Id));
            Assert.That(tertiaryTransitions.Select(transition => transition.Objective.Id),
                Does.Contain(tertiary.Id));
            Assert.That(runtime.GetState(secondary.Id),
                Is.EqualTo(MechRewired.Missions.MissionObjectiveState.Completed));
            Assert.That(runtime.GetState(tertiary.Id),
                Is.EqualTo(MechRewired.Missions.MissionObjectiveState.Completed));
            Assert.That(extraction.PrerequisiteIds,
                Is.EquivalentTo(new[] { "mtbl-0-1", "mtbl-0-2" }));
            Assert.That(navigationReports["Pinknav1"], Is.EqualTo("genegoaS"));
            Assert.That(navigationReports["Pinknav2"], Is.EqualTo("genegobS"));
            Assert.That(navigationReports["Pinknav3"], Is.EqualTo("genegocS"));
        });
    }

    [Test]
    public void JadeFalconReconHelicopterIsOneMovingDamageableAssembly()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/PINKSCN1.BWD");
        var level = MechWarriorLevel.Load(
            archive,
            mission.Level.Entry.Path,
            include =>
            {
                var includedWorld = MechWarriorWorldFile.Load(
                    archive.ReadEntry(archive.GetEntry("BWD", include.ResourceIndex)));
                return !HasTaskArgument(includedWorld, "drop");
            });

        var source = level.Sources.Single(candidate =>
            candidate.Entry.Name.Equals("PINKHELO.BWD", StringComparison.OrdinalIgnoreCase));
        var helicopterActors = level.Actors.Where(actor =>
            actor.SourceEntry.Path.Equals(source.Entry.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        var plan = MechWarriorAuthoredAircraftResolver.Resolve(level).Single(candidate =>
            candidate.Source.Entry.Path.Equals(source.Entry.Path, StringComparison.OrdinalIgnoreCase));
        var routePoints = plan.Path.Points;
        var routeDurationSeconds = routePoints
            .Take(routePoints.Count - 1)
            .Sum(point => point.TravelSeconds);
        var routeDistanceMeters = routePoints
            .Take(routePoints.Count - 1)
            .Select((point, index) => System.Numerics.Vector3.Distance(
                point.Position,
                routePoints[index + 1].Position))
            .Sum();
        var averageRouteSpeedKph = routeDistanceMeters / routeDurationSeconds * 3.6f;
        var rotorVertices = MechWarriorModel.LoadAll(archive.ReadEntry(plan.RotorComponent.ModelEntry))
            .SelectMany(model => model.Vertices)
            .Select(vertex => vertex.Position)
            .ToArray();
        var rotorBoundsCenter = new System.Numerics.Vector3(
            (rotorVertices.Min(vertex => vertex.X) + rotorVertices.Max(vertex => vertex.X)) * 0.005f,
            (rotorVertices.Min(vertex => vertex.Y) + rotorVertices.Max(vertex => vertex.Y)) * 0.005f,
            (rotorVertices.Min(vertex => vertex.Z) + rotorVertices.Max(vertex => vertex.Z)) * 0.005f);

        Assert.Multiple(() =>
        {
            Assert.That(helicopterActors, Has.Length.EqualTo(1),
                "Zero-health rotor/wreckage records must not become independent live actors.");
            Assert.That(helicopterActors[0].ObjectId, Is.EqualTo(2));
            Assert.That(helicopterActors[0].Health, Is.EqualTo(30));
            Assert.That(helicopterActors[0].Description, Is.EqualTo("Recon Helicopter"));
            Assert.That(helicopterActors[0].DetailDescription, Is.EqualTo("Recon Pilot"));
            Assert.That(
                helicopterActors[0].Components.Select(component => component.ModelEntry.Name),
                Is.EquivalentTo(new[] { "V_BHELOA.WTB", "V_BHELOB.WTB" }));
            Assert.That(
                helicopterActors[0].DestroyedComponents.Select(component => component.ModelEntry.Name),
                Does.Contain("VCDHELOA.WTB"));
            Assert.That(source.World.PathTables.Single(table => table.Name == "recon").Points, Has.Count.EqualTo(11));
            Assert.That(routePoints[0].TravelTicks, Is.EqualTo(1820));
            Assert.That(routePoints[0].TravelSeconds, Is.EqualTo(10.0f).Within(0.001f));
            Assert.That(routeDurationSeconds, Is.EqualTo(46.0f).Within(0.001f));
            Assert.That(averageRouteSpeedKph, Is.EqualTo(246.8f).Within(0.2f),
                "The fast recon pass is authored data; a tick-rate regression must not silently retune it.");
            Assert.That(plan.Actor.ObjectId, Is.EqualTo(2));
            Assert.That(plan.MotionObject.Id, Is.EqualTo(0));
            Assert.That(plan.Path.Name, Is.EqualTo("recon"));
            Assert.That(plan.RotateWithPath, Is.True);
            Assert.That(plan.Path.Points[0].Position.X, Is.EqualTo(1102.31f).Within(0.01f));
            Assert.That(plan.Path.Points[0].Position.Y, Is.EqualTo(10.0f).Within(0.01f));
            Assert.That(plan.Path.Points[0].Position.Z, Is.EqualTo(462.52f).Within(0.01f));
            Assert.That(plan.SoundObjectId, Is.EqualTo(2));
            Assert.That(plan.MaximumSoundDistance, Is.EqualTo(500.0f));
            Assert.That(plan.SoundResourceName, Is.EqualTo("HELICPTR"));
            Assert.That(plan.LoopSound, Is.True);
            Assert.That(plan.RotorComponent.Id, Is.EqualTo(4));
            Assert.That(rotorBoundsCenter.X, Is.EqualTo(2.53f).Within(0.01f));
            Assert.That(rotorBoundsCenter.Y, Is.EqualTo(4.77f).Within(0.01f));
            Assert.That(rotorBoundsCenter.Z, Is.EqualTo(0.205f).Within(0.01f),
                "The rotor hub is not the WTB origin; runtime rotation must preserve this center.");
            Assert.That(plan.Actor.Components.Single(component => component.Id == 2).Transform.Translation.X,
                Is.EqualTo(1214.51f).Within(0.01f));
            Assert.That(plan.Actor.Components.Single(component => component.Id == 2).Transform.Translation.Y,
                Is.EqualTo(68.33f).Within(0.01f));
            Assert.That(plan.Actor.Components.Single(component => component.Id == 2).Transform.Translation.Z,
                Is.EqualTo(450.43f).Within(0.01f));
            Assert.That(plan.Actor.DestroyedComponents.Single(component => component.Id == 3).Transform.Translation.Y,
                Is.EqualTo(38.33f).Within(0.01f));
        });
    }

    [Test]
    public void SilentThunderTypeFivePathsResolveFromOriginalBwdTables()
    {
        var archive = OpenOriginalArchive();
        var craneWorld = MechWarriorWorldFile.Load(
            archive.ReadEntry(archive.GetEntry("BWD/PINKARE3.BWD")));
        var steamWorld = MechWarriorWorldFile.Load(
            archive.ReadEntry(archive.GetEntry("BWD/PINKARE5.BWD")));
        var uplinkWorld = MechWarriorWorldFile.Load(
            archive.ReadEntry(archive.GetEntry("BWD/PINKARE1.BWD")));
        var cranePlans = craneWorld.Tasks
            .Where(task => task.Type == 5)
            .Select(task => ResolvePath(craneWorld, task))
            .ToArray();
        var uplinkPlans = uplinkWorld.Tasks
            .Where(task => task.Type == 5)
            .Select(task => ResolvePath(uplinkWorld, task))
            .ToArray();
        var uplinkEffects = uplinkWorld.Objects
            .Where(worldObject => worldObject.ObjectType == 0x10)
            .Select(worldObject => (worldObject.Id, Model: archive.GetEntry("POLY", worldObject.ModelResourceIndex).Name,
                worldObject.RelativeToId, worldObject.Transform.Translation))
            .ToArray();
        var pulseModel = MechWarriorModel.LoadAll(
            archive.ReadEntry(archive.GetEntry("POLY/PULSE.WTB")))[0];
        var steamControls = steamWorld.Objects
            .Where(worldObject => worldObject.ObjectType == 0x10)
            .ToArray();
        var craneObjectsById = craneWorld.Objects.ToDictionary(worldObject => worldObject.Id);
        var craneDescendantIds = craneWorld.Objects
            .Where(worldObject => IsDescendantOf(
                worldObject.Id,
                cranePlans[0].MotionObjectId,
                craneObjectsById))
            .Select(worldObject => worldObject.Id)
            .ToHashSet();
        var craneSoundObjectIds = craneWorld.Tasks
            .Where(task => task.Type == 4)
            .Select(task => task.Command.Split(';', 2)[0])
            .Where(argument => int.TryParse(argument, out _))
            .Select(int.Parse)
            .Where(craneDescendantIds.Contains)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(cranePlans, Has.Length.EqualTo(1));
            Assert.That(cranePlans[0].Playback, Is.EqualTo(MechWarriorWorldPathPlayback.Loop));
            Assert.That(cranePlans[0].RotateWithPath, Is.False);
            Assert.That(cranePlans[0].Path.Name, Is.EqualTo("crane"));
            Assert.That(cranePlans[0].Path.Points, Has.Count.EqualTo(15));
            Assert.That(craneWorld.Entities.Select(entity => entity.ObjectId),
                Does.Contain(cranePlans[0].MotionObjectId),
                "Crane motion and its attached sound must share the crane actor's destruction lifetime.");
            Assert.That(craneSoundObjectIds, Is.Not.Empty,
                "The crane path must retain its authored positional machinery sound.");
            Assert.That(steamControls.Select(control => control.RelativeToId),
                Is.SubsetOf(steamWorld.Entities.Select(entity => entity.ObjectId)),
                "Each steam control is attached directly to a destructible relief assembly.");
            Assert.That(uplinkPlans, Has.Length.EqualTo(6));
            Assert.That(uplinkPlans.Select(plan => plan.Playback),
                Is.All.EqualTo(MechWarriorWorldPathPlayback.Repeat));
            Assert.That(uplinkPlans.Select(plan => plan.Path.Name),
                Is.EquivalentTo(new[] { "trnmove", "trnmove2", "trnmove3", "trnmove4", "pulshot", "sndshot" }));
            Assert.That(uplinkPlans.Single(plan => plan.Path.Name == "pulshot").Path.Points.Last().Position.Y,
                Is.EqualTo(2000.0f).Within(0.01f));
            Assert.That(uplinkEffects.Select(effect => effect.Model),
                Is.EqualTo(new[] { "FIR2_1.WTB" }),
                "The HPG source carries one launch-flash control volume, not a persistent combustion site.");
            Assert.That(pulseModel.Vertices, Has.Count.EqualTo(3));
            Assert.That(pulseModel.Polygons.Select(polygon => polygon.VertexIndices),
                Is.EquivalentTo(new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 } }),
                "PULSE.WTB is a two-sided triangular effect primitive, not a complete projectile sprite.");
        });
    }

    [Test]
    public void SilentThunderDestructionHierarchyMatchesFacilityObjectives()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/PINKSCN1.BWD");
        var definition = MechRewired.Missions.MissionDefinition.FromMissionTable(
            mission.Scenario.MissionTables.Single(table => table.Index == 0));
        var level = MechWarriorLevel.Load(archive, mission.Level.Entry.Path);
        var links = MechWarriorActorDestructionLinkResolver.Resolve(level);
        var roots = MechWarriorActorHierarchyResolver.ResolveRoots(level);
        var alphaActors = level.Actors.Where(actor => actor.SourceEntry.Name == "PINKARE1.BWD").ToArray();
        var alphaLinks = links.Where(link => link.Parent.SourceEntry.Name == "PINKARE1.BWD").ToArray();
        var alphaRoot = alphaActors.Single(actor => actor.ObjectId == 2);
        var alphaCascade = GetCascade(alphaRoot, alphaLinks);
        var betaActors = level.Actors.Where(actor => actor.SourceEntry.Name == "PINKARE2.BWD").ToArray();
        var betaLinks = links.Where(link => link.Parent.SourceEntry.Name == "PINKARE2.BWD").ToArray();
        var betaChildren = betaLinks.Select(link => link.Child).ToHashSet();
        var betaRootIds = betaActors
            .Where(actor => !betaChildren.Contains(actor))
            .Select(actor => actor.ObjectId)
            .Order()
            .ToArray();
        var betaFireControl = level.EffectObjects.Single(levelObject =>
            levelObject.SourceEntry.Name == "PINKARE2.BWD" &&
            levelObject.ModelEntry.Name == "FIR2_1.WTB");

        Assert.Multiple(() =>
        {
            Assert.That(definition.Objectives.Single(objective => objective.TargetResourceName == "pinkare1")
                .SuccessReport.Name, Is.EqualTo("pink001S"));
            Assert.That(alphaRoot.Description, Is.EqualTo("HPG-Uplink"));
            Assert.That(alphaRoot.DetailDescription, Is.EqualTo("Main Firing Chamber"));
            Assert.That(alphaLinks, Has.Length.EqualTo(12));
            Assert.That(alphaCascade, Is.EquivalentTo(alphaActors),
                "Destroying the HPG root must propagate through every nested gun, processor, and wall.");
            Assert.That(alphaActors.Select(actor => roots[actor]), Is.All.SameAs(alphaRoot),
                "Objective targeting must identify the HPG root rather than one of its component walls.");
            Assert.That(betaLinks, Has.Length.EqualTo(3));
            Assert.That(betaRootIds, Is.EqualTo(new[] { 1, 3, 12, 18 }),
                "The communications array has four independently destroyed authored roots.");
            Assert.That(betaFireControl.Id, Is.EqualTo(4));
            Assert.That(level.StaticObjects, Does.Not.Contain(betaFireControl),
                "Type 0x10 effect controls must not leak into opaque scenery, collision, or targeting.");
        });
    }

    [Test]
    public void SilentThunderDeploysTwoAuthoredPulseLaserTurrets()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/PINKSCN1.BWD");
        var gamePieces = MechWarriorMissionGamePieceLoader.Load(archive, mission.Scenario);
        var turrets = gamePieces
            .Where(gamePiece => gamePiece.SourceEntry.Name is "PINKENT1.BWD" or "PINKENT2.BWD")
            .OrderBy(gamePiece => gamePiece.SourceEntry.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(gamePieces, Has.Count.EqualTo(5),
                "Silent Thunder deploys three Kit Foxes and two fixed turrets.");
            Assert.That(turrets, Has.Length.EqualTo(2));
            Assert.That(turrets.Select(turret => turret.Star.Disposition),
                Is.All.EqualTo(MechWarriorMissionDisposition.Hostile));
            Assert.That(turrets.Select(turret => turret.Specification.ChassisName),
                Is.All.EqualTo("turretg"));
            Assert.That(turrets.Select(turret => turret.Specification.ConfigurationName),
                Is.All.EqualTo("tur00mpl"));
            Assert.That(turrets.Select(turret => turret.Specification.DisplayName),
                Is.All.EqualTo("Turret"));
            Assert.That(turrets.Select(turret => turret.SpawnPoint.GroupId), Is.All.Zero,
                "The turret NAVPs are linked by MTBL index rather than their local group field.");
            Assert.That(System.Numerics.Vector3.Distance(
                turrets[0].SpawnPoint.Position,
                new System.Numerics.Vector3(928.45f, 0.0f, -1447.01f)), Is.LessThan(0.01f));
            Assert.That(System.Numerics.Vector3.Distance(
                turrets[1].SpawnPoint.Position,
                new System.Numerics.Vector3(1060.83f, 0.0f, -1428.30f)), Is.LessThan(0.01f));
        });

        foreach (var turret in turrets)
        {
            var chassis = MechWarriorMechChassis.Load(archive.ReadEntry(turret.ChassisEntry));
            var configuration = MechWarriorMechFile.Load(archive.ReadEntry(turret.ConfigurationEntry));
            var objectsById = chassis.Objects.ToDictionary(chassisObject => chassisObject.Id);
            Assert.Multiple(() =>
            {
                Assert.That(chassis.Objects, Is.Not.Empty);
                Assert.That(chassis.ThingObjectIds, Is.EqualTo(new[] { 6, 7 }),
                    "The original turret supplies separate yaw and pitch joints.");
                Assert.That(objectsById[7].RelativeToId, Is.EqualTo(6));
                Assert.That(chassis.PointsOfFire.Select(point => point.ObjectId),
                    Is.EquivalentTo(new[] { 9, 15, 11, 13 }));
                Assert.That(chassis.PointsOfFire.All(point =>
                    IsDescendantOf(point.ObjectId, 7, objectsById)), Is.True);
                Assert.That(configuration.WalkingMovementPoints, Is.Zero);
                Assert.That(configuration.Weapons.Select(weapon => weapon.SourceId),
                    Is.EqualTo(new ushort[] { 2601, 2602, 2603, 2604 }));
                Assert.That(configuration.Weapons.Select(weapon => weapon.Specification.Kind),
                    Is.All.EqualTo(MechWeaponKind.PulseLaser));
            });
        }
    }

    [Test]
    public void WolfMissionAuthoredInventoryReconcilesWithACompleteRuntimeInventory()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/YELLSCN1.BWD");
        var definition = MechRewired.Missions.MissionDefinition.FromMissionTable(
            mission.Scenario.MissionTables.Single(table => table.Index == 0));
        var level = MechWarriorLevel.Load(archive, mission.Level.Entry.Path);
        var gamePieces = MechWarriorMissionGamePieceLoader.Load(archive, mission.Scenario);
        var navigationPoints = mission.NavigationPoints.Select(resource =>
        {
            var world = MechWarriorWorldFile.Load(
                archive.ReadEntry(resource.Entry), resource.Include.Transform);
            return new MechWarriorMissionNavigationPoint(
                Path.GetFileNameWithoutExtension(resource.Entry.Name), world.NavPoints.Single());
        }).ToArray();
        var runtime = CompleteRuntimeInventory(level, navigationPoints, definition, gamePieces);
        var audit = MechRewired.Missions.MissionFidelityAudit.Analyze(
            mission, level, navigationPoints, definition, gamePieces, runtime);
        var hostileGroups = gamePieces
            .Where(piece => piece.Star.Disposition == MechWarriorMissionDisposition.Hostile)
            .Select(piece => piece.Specification.GroupId)
            .Order()
            .ToArray();
        var secondary = definition.Objectives.Single(objective =>
            objective.Description == "Destroy All Surviving Mechs");
        var extraction = definition.Objectives.Single(objective =>
            objective.Kind == MechRewired.Missions.MissionObjectiveKind.Extract);
        var navigationReports = definition.EventReports
            .Where(report => report.Trigger.Kind == MechRewired.Missions.MissionEventKind.NavigationPointReached)
            .ToDictionary(
                report => report.Trigger.TargetResourceName,
                report => report.Report.Name,
                StringComparer.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(mission.MissionPrefix, Is.EqualTo("YELL"));
            Assert.That(mission.Level.Entry.Name, Is.EqualTo("YELLWLD1.BWD"));
            Assert.That(gamePieces, Has.Count.EqualTo(4));
            Assert.That(hostileGroups, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(level.Actors, Has.Count.EqualTo(9));
            Assert.That(navigationPoints, Has.Length.EqualTo(3));
            Assert.That(definition.Objectives, Has.Count.EqualTo(5));
            Assert.That(definition.TimeLimitSeconds, Is.EqualTo(1500));
            Assert.That(secondary.SuccessReport.Name, Is.EqualTo("gene002S"));
            Assert.That(secondary.AggregateRequirements.Select(requirement => requirement.TargetResourceName),
                Is.EquivalentTo(new[] { "yellens1", "yellens2", "yellens3", "yellens4", "yellens5" }));
            Assert.That(extraction.PrerequisiteIds,
                Is.EquivalentTo(new[] { "mtbl-0-6", "mtbl-0-7" }));
            Assert.That(navigationReports["yellNAV1"], Is.EqualTo("genegoeS"));
            Assert.That(navigationReports["yellNAV2"], Is.EqualTo("genegofS"));
            Assert.That(navigationReports["yellNAV3"], Is.EqualTo("genegogS"));
            Assert.That(audit.Findings.Where(finding =>
                finding.Kind == MechRewired.Missions.MissionFidelityFindingKind.MissingRuntimeContent), Is.Empty);
        });
    }

    [Test]
    public void SilentThunderAuditRequiresTurretsAndReconAircraftAtRuntime()
    {
        var archive = OpenOriginalArchive();
        var mission = MechWarriorMissionResources.Load(archive, "BWD/PINKSCN1.BWD");
        var definition = MechRewired.Missions.MissionDefinition.FromMissionTable(
            mission.Scenario.MissionTables.Single(table => table.Index == 0));
        var level = MechWarriorLevel.Load(archive, mission.Level.Entry.Path);
        var gamePieces = MechWarriorMissionGamePieceLoader.Load(archive, mission.Scenario);
        var navigationPoints = mission.NavigationPoints.Select(resource =>
        {
            var world = MechWarriorWorldFile.Load(
                archive.ReadEntry(resource.Entry), resource.Include.Transform);
            return new MechWarriorMissionNavigationPoint(
                Path.GetFileNameWithoutExtension(resource.Entry.Name), world.NavPoints.Single());
        }).ToArray();
        var runtime = CompleteRuntimeInventory(level, navigationPoints, definition, gamePieces);
        var completeAudit = MechRewired.Missions.MissionFidelityAudit.Analyze(
            mission, level, navigationPoints, definition, gamePieces, runtime);
        var helicopter = level.Actors.Single(actor => actor.SourceEntry.Name == "PINKHELO.BWD");
        runtime = new MechRewired.Missions.MissionRuntimeContent();
        foreach (var actor in level.Actors)
        {
            runtime.AddActorRepresentation(actor, false);
            if (actor.DestroyedComponents.Count > 0) runtime.AddActorRepresentation(actor, true);
        }
        foreach (var point in navigationPoints) runtime.AddNavigationPoint(point);
        foreach (var objective in definition.Objectives) runtime.AddObjective(objective);
        foreach (var effect in level.EffectObjects) runtime.AddEffect(effect);
        foreach (var piece in gamePieces.Where(piece => piece.Star.Disposition == MechWarriorMissionDisposition.Hostile)
                     .Where(piece => piece.SourceEntry.Name != "PINKENT2.BWD")) runtime.AddCombatant(piece);
        var audit = MechRewired.Missions.MissionFidelityAudit.Analyze(
            mission, level, navigationPoints, definition, gamePieces, runtime);

        Assert.Multiple(() =>
        {
            Assert.That(completeAudit.Findings.Where(finding =>
                finding.Kind == MechRewired.Missions.MissionFidelityFindingKind.MissingRuntimeContent), Is.Empty);
            Assert.That(audit.Findings.Any(finding =>
                finding.Kind == MechRewired.Missions.MissionFidelityFindingKind.MissingRuntimeContent &&
                finding.Identity.Contains("PINKENT2.BWD", StringComparison.OrdinalIgnoreCase)), Is.True,
                "The second authored Jade turret must not silently disappear.");
            Assert.That(audit.Findings.Any(finding =>
                finding.Kind == MechRewired.Missions.MissionFidelityFindingKind.MissingRuntimeContent &&
                finding.Identity.Contains("PINKHELO.BWD", StringComparison.OrdinalIgnoreCase)), Is.True,
                "The authored helicopter path must not silently disappear.");
            Assert.That(helicopter.Description, Is.EqualTo("Recon Helicopter"));
        });
    }

    private static MechRewired.Missions.MissionRuntimeContent CompleteRuntimeInventory(
        MechWarriorLevel level,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        MechRewired.Missions.MissionDefinition definition,
        IReadOnlyList<MechWarriorMissionGamePiece> gamePieces)
    {
        var runtime = new MechRewired.Missions.MissionRuntimeContent();
        foreach (var actor in level.Actors)
        {
            runtime.AddActorRepresentation(actor, false);
            if (actor.DestroyedComponents.Count > 0) runtime.AddActorRepresentation(actor, true);
        }
        foreach (var point in navigationPoints) runtime.AddNavigationPoint(point);
        foreach (var objective in definition.Objectives) runtime.AddObjective(objective);
        foreach (var piece in gamePieces.Where(piece => piece.Star.Disposition == MechWarriorMissionDisposition.Hostile)) runtime.AddCombatant(piece);
        foreach (var effect in level.EffectObjects) runtime.AddEffect(effect);
        foreach (var plan in MechWarriorAuthoredAircraftResolver.Resolve(level)) runtime.AddAircraft(plan.Actor);
        return runtime;
    }

    private static bool IsDescendantOf(
        int objectId,
        int ancestorId,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById)
    {
        for (var currentId = objectId; objectsById.TryGetValue(currentId, out var current); currentId = current.RelativeToId)
        {
            if (currentId == ancestorId)
            {
                return true;
            }

            if (current.RelativeToId < 0)
            {
                return false;
            }
        }

        return false;
    }

    private static MechWarriorWorldPathTask ResolvePath(
        MechWarriorWorldFile world,
        MechWarriorWorldTask task)
    {
        Assert.That(MechWarriorWorldPathTask.TryResolve(world, task, out var plan, out var error), Is.True, error);
        return plan;
    }

    private static IReadOnlySet<MechWarriorLevelActor> GetCascade(
        MechWarriorLevelActor root,
        IReadOnlyList<MechWarriorActorDestructionLink> links)
    {
        var actors = new HashSet<MechWarriorLevelActor> { root };
        var pending = new Queue<MechWarriorLevelActor>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var parent))
        {
            foreach (var child in links.Where(link => ReferenceEquals(link.Parent, parent)).Select(link => link.Child))
            {
                if (actors.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return actors;
    }

    private static MechWarriorProjectArchive OpenOriginalArchive()
    {
        var path = Environment.GetEnvironmentVariable(ArchiveEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "local", "game-data", "MW2.PRJ"));
        }

        if (!File.Exists(path))
        {
            Assert.Ignore(
                $"Set {ArchiveEnvironmentVariable} to a licensed MW2.PRJ to run original-data integration checks.");
        }

        return MechWarriorProjectArchive.Open(new FileInfo(path));
    }

    private static bool HasTaskArgument(MechWarriorWorldFile world, string argument) =>
        world.Tasks.Any(task =>
            task.Command.Split([';', ','], StringSplitOptions.TrimEntries)
                .Any(candidate => candidate.Equals(argument, StringComparison.OrdinalIgnoreCase)));
}
