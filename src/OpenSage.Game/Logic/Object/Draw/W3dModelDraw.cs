﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Data.Ini;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.Graphics.Cameras;
using OpenSage.Graphics.ParticleSystems;
using OpenSage.Graphics.Rendering;
using OpenSage.Graphics.Shaders;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object
{
    public class W3dModelDraw : DrawModule
    {
        private readonly W3dModelDrawModuleData _data;
        private readonly GameContext _context;

        private readonly List<ModelConditionState> _conditionStates;
        private readonly ModelConditionState _defaultConditionState;

        private readonly List<AnimationState> _animationStates;
        private readonly AnimationState _idleAnimationState;

        private readonly Dictionary<ModelConditionState, W3dModelDrawConditionState> _cachedModelDrawConditionStates;

        private ModelConditionState _activeConditionState;
        private AnimationState _activeAnimationState;

        private W3dModelDrawConditionState _activeModelDrawConditionState;
        private float _sinkFactor;

        protected ModelInstance ActiveModelInstance => _activeModelDrawConditionState.Model;

        public override IEnumerable<BitArray<ModelConditionFlag>> ModelConditionStates
        {
            get
            {
                yield return _defaultConditionState.ConditionFlags;

                foreach (var conditionState in _conditionStates)
                {
                    yield return conditionState.ConditionFlags;
                }

                foreach (var animationState in _animationStates)
                {
                    yield return animationState.TypeFlags;
                }
            }
        }

        internal override string GetWeaponFireFXBone(WeaponSlot slot)
            => _defaultConditionState?.WeaponFireFXBones.Find(x => x.WeaponSlot == slot)?.BoneName;

        internal override string GetWeaponLaunchBone(WeaponSlot slot)
            => _defaultConditionState?.WeaponLaunchBones.Find(x => x.WeaponSlot == slot)?.BoneName;

        internal W3dModelDraw(
            W3dModelDrawModuleData data,
            GameContext context)
        {
            _data = data;
            _context = context;

            _conditionStates = new List<ModelConditionState>();

            if (data.DefaultConditionState != null)
            {
                _defaultConditionState = data.DefaultConditionState;
            }

            foreach (var conditionState in data.ConditionStates)
            {
                _conditionStates.Add(conditionState);
            }

            if (_defaultConditionState == null)
            {
                _defaultConditionState = _conditionStates.Find(x => !x.ConditionFlags.AnyBitSet);

                if (_defaultConditionState != null)
                {
                    _conditionStates.Remove(_defaultConditionState);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            _cachedModelDrawConditionStates = new Dictionary<ModelConditionState, W3dModelDrawConditionState>();

            SetActiveConditionState(_defaultConditionState);

            _animationStates = new List<AnimationState>();

            if (data.IdleAnimationState != null)
            {
                _idleAnimationState = data.IdleAnimationState;
            }

            foreach (var animationState in data.AnimationStates)
            {
                _animationStates.Add(animationState);
            }
        }

        private void SetActiveConditionState(ModelConditionState conditionState)
        {
            if (_activeConditionState == conditionState)
            {
                return;
            }

            _activeModelDrawConditionState?.Deactivate();

            if (!_cachedModelDrawConditionStates.TryGetValue(conditionState, out var modelDrawConditionState))
            {
                modelDrawConditionState = AddDisposable(CreateModelDrawConditionStateInstance(conditionState));
                _cachedModelDrawConditionStates.Add(conditionState, modelDrawConditionState);
            }

            _activeConditionState = conditionState;
            _activeModelDrawConditionState = modelDrawConditionState;

            _activeModelDrawConditionState?.Activate();
        }

        private void SetActiveAnimationState(AnimationState animationState)
        {
            if (_activeAnimationState == animationState
             || _activeModelDrawConditionState == null)
            {
                return;
            }

            _activeAnimationState = animationState;

            var modelInstance = _activeModelDrawConditionState.Model;

            var firstAnimationBlock = animationState.Animations.FirstOrDefault();
            if (firstAnimationBlock != null)
            {
                foreach(var animation in firstAnimationBlock.Animations)
                {
                    var anim = animation.Value;
                    //Check if the animation does really exist
                    if(anim != null)
                    {
                        var flags = animationState.Flags;
                        var mode = firstAnimationBlock.AnimationMode;
                        var animationInstance = new AnimationInstance(modelInstance, anim, mode, flags);
                        modelInstance.AnimationInstances.Add(animationInstance);
                        animationInstance.Play();
                        break;
                    }
                }
            }
        }

        public override void UpdateConditionState(BitArray<ModelConditionFlag> flags)
        {
            ModelConditionState bestConditionState = null;
            var bestMatch = int.MinValue;

            // Find best matching ModelConditionState.
            foreach (var conditionState in _conditionStates)
            {
                var numStateBits = conditionState.ConditionFlags.NumBitsSet;
                var numIntersectionBits = conditionState.ConditionFlags.CountIntersectionBits(flags);

                // If there's no intersection never select this.
                if (numIntersectionBits != numStateBits)
                {
                    continue;
                }

                if (numIntersectionBits > bestMatch)
                {
                    bestConditionState = conditionState;
                    bestMatch = numIntersectionBits;
                }
            }

            if (bestConditionState == null || bestMatch == 0)
            {
                bestConditionState = _defaultConditionState;
            }

            SetActiveConditionState(bestConditionState);

            foreach (var weaponMuzzleFlash in bestConditionState.WeaponMuzzleFlashes)
            {
                var visible = flags.Get(ModelConditionFlag.FiringA);
                for (var i = 0; i < _activeModelDrawConditionState.Model.ModelBoneInstances.Length; i++)
                {
                    var bone = _activeModelDrawConditionState.Model.ModelBoneInstances[i];
                    // StartsWith is a bit awkward here, but for instance AVCommance has WeaponMuzzleFlashes = { TurretFX }, and Bones = { TURRETFX01 }
                    if (bone.Name.StartsWith(weaponMuzzleFlash.BoneName.ToUpper()))
                    {
                        _activeModelDrawConditionState.Model.BoneVisibilities[i] = visible;
                    }
                }
            };
            
            AnimationState bestAnimationState = null;
            bestMatch = int.MinValue;

            // Find best matching ModelConditionState.
            foreach (var animationState in _animationStates)
            {
                var numStateBits = animationState.TypeFlags.NumBitsSet;
                var numIntersectionBits = animationState.TypeFlags.CountIntersectionBits(flags);

                // If there's no intersection never select this.
                if (numIntersectionBits != numStateBits)
                {
                    continue;
                }

                if (numIntersectionBits > bestMatch)
                {
                    bestAnimationState = animationState;
                    bestMatch = numIntersectionBits;
                }
            }

            if (bestAnimationState == null || bestMatch == 0)
            {
                bestAnimationState = _idleAnimationState;
            }

            SetActiveAnimationState(bestAnimationState);
        }

        private W3dModelDrawConditionState CreateModelDrawConditionStateInstance(ModelConditionState conditionState)
        {
            // Load model, fallback to default model.
            var model = conditionState.Model?.Value ?? _defaultConditionState.Model?.Value;

            ModelInstance modelInstance = null;
            if (model != null)
            {
                modelInstance = model.CreateInstance(_context.AssetLoadContext);
            }

            if (modelInstance != null)
            {
                // TODO: Multiple animations. Shouldn't play all of them. I think
                // we should randomly choose one of them?
                // And there is also IdleAnimation.
                var firstAnimation = conditionState.ConditionAnimations
                    .Concat(conditionState.IdleAnimations)
                    .LastOrDefault();
                if (firstAnimation != null)
                {
                    var animation = firstAnimation.Animation.Value;

                    if (animation != null)
                    {
                        var mode = conditionState.AnimationMode;
                        var flags = conditionState.Flags;
                        var animationInstance = new AnimationInstance(modelInstance, animation, mode, flags);
                        modelInstance.AnimationInstances.Add(animationInstance);
                    }
                }
            }

            var particleSystems = new List<ParticleSystem>();
            if (modelInstance != null)
            {
                foreach (var particleSysBone in conditionState.ParticleSysBones)
                {
                    var particleSystemTemplate = particleSysBone.ParticleSystem.Value;
                    if (particleSystemTemplate == null)
                    {
                        throw new InvalidOperationException();
                    }

                    var bone = modelInstance.Model.BoneHierarchy.Bones.FirstOrDefault(x => string.Equals(x.Name, particleSysBone.BoneName, StringComparison.OrdinalIgnoreCase));
                    if (bone == null)
                    {
                        // TODO: Should this ever happen?
                        continue;
                    }

                    particleSystems.Add(_context.ParticleSystems.Create(
                        particleSystemTemplate,
                        () => ref modelInstance.AbsoluteBoneTransforms[bone.Index]));
                }
            }

            if (modelInstance != null)
            {
                if (conditionState.HideSubObject != null)
                {
                    foreach (var hideSubObject in conditionState.HideSubObject)
                    {
                        var item = modelInstance.ModelBoneInstances.Select((value, i) => new { i, value }).FirstOrDefault(x => x.value.Name.EndsWith("." + hideSubObject.ToUpper()));
                        if (item != null)
                        {
                            modelInstance.BoneVisibilities[item.i] = false;
                        }

                    }
                }
                if (conditionState.ShowSubObject != null)
                {
                    foreach (var showSubObject in conditionState.ShowSubObject)
                    {
                        var item = modelInstance.ModelBoneInstances.Select((value, i) => new { i, value }).FirstOrDefault(x => x.value.Name.EndsWith("." + showSubObject.ToUpper()));
                        if (item != null)
                        {
                            modelInstance.BoneVisibilities[item.i] = true;
                        }

                    }
                }
            }

            return modelInstance != null
               ? new W3dModelDrawConditionState(modelInstance, particleSystems, _context)
               : null;
        }

        internal override (ModelInstance, ModelBone) FindBone(string boneName)
        {
            return (ActiveModelInstance, ActiveModelInstance.Model.BoneHierarchy.Bones.First(x => string.Equals(x.Name, boneName, StringComparison.OrdinalIgnoreCase)));
        }

        internal override void Update(in TimeInterval gameTime, GameObject gameObject)
        {
            if(_activeConditionState.Flags.HasFlag(AnimationFlags.AdjustHeightByConstructionPercent))
            {
                //TODO: change the world matrix?
                float progress = gameObject.BuildProgress;
                _sinkFactor = (1.0f - progress) * gameObject.Collider.Height;
            }

            _activeModelDrawConditionState?.Update(gameTime);
        }

        internal override void SetWorldMatrix(in Matrix4x4 worldMatrix)
        {
            if (_activeConditionState.Flags.HasFlag(AnimationFlags.AdjustHeightByConstructionPercent))
            {
                var mat = worldMatrix * Matrix4x4.CreateTranslation(-Vector3.UnitZ * _sinkFactor);// // _sinkFactor;
                _activeModelDrawConditionState?.SetWorldMatrix(mat);
            }
            else
            {
                _activeModelDrawConditionState?.SetWorldMatrix(worldMatrix);
            }
        }

        internal override void BuildRenderList(
            RenderList renderList,
            Camera camera,
            bool castsShadow,
            MeshShaderResources.RenderItemConstantsPS renderItemConstantsPS)
        {
            _activeModelDrawConditionState?.BuildRenderList(
                renderList,
                camera,
                castsShadow,
                renderItemConstantsPS);
        }
    }

    internal sealed class W3dModelDrawConditionState : DisposableBase
    {
        private readonly IEnumerable<ParticleSystem> _particleSystems;
        private readonly GameContext _context;

        public readonly ModelInstance Model;

        public W3dModelDrawConditionState(
            ModelInstance modelInstance,
            IEnumerable<ParticleSystem> particleSystems,
            GameContext context)
        {
            Model = AddDisposable(modelInstance);

            _particleSystems = particleSystems;
            _context = context;
        }

        public void Activate()
        {
            foreach (var animationInstance in Model.AnimationInstances)
            {
                animationInstance.Play();
            }

            foreach (var particleSystem in _particleSystems)
            {
                particleSystem.Activate();
            }
        }

        public void Deactivate()
        {
            foreach (var animationInstance in Model.AnimationInstances)
            {
                animationInstance.Stop();
            }

            foreach (var particleSystem in _particleSystems)
            {
                particleSystem.Deactivate();
            }
        }

        public void Update(in TimeInterval gameTime)
        {
            Model.Update(gameTime);
        }

        public void SetWorldMatrix(in Matrix4x4 worldMatrix)
        {
            Model.SetWorldMatrix(worldMatrix);
        }

        public void BuildRenderList(
            RenderList renderList,
            Camera camera,
            bool castsShadow,
            MeshShaderResources.RenderItemConstantsPS renderItemConstantsPS)
        {
            Model.BuildRenderList(
                renderList,
                camera,
                castsShadow,
                renderItemConstantsPS);
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            foreach (var particleSystem in _particleSystems)
            {
                _context.ParticleSystems.Remove(particleSystem);
            }

            base.Dispose(disposeManagedResources);
        }
    }

    public class W3dModelDrawModuleData : DrawModuleData
    {
        internal static W3dModelDrawModuleData ParseModel(IniParser parser) => parser.ParseBlock(FieldParseTable);

        internal static readonly IniParseTable<W3dModelDrawModuleData> FieldParseTable = new IniParseTable<W3dModelDrawModuleData>
        {
            { "DefaultConditionState", (parser, x) => parser.Temp = x.DefaultConditionState = ModelConditionState.ParseDefault(parser) },
            { "DefaultModelConditionState", (parser, x) => parser.Temp = x.DefaultConditionState = ModelConditionState.ParseDefault(parser) },

            {
                "ConditionState",
                (parser, x) =>
                {
                    var conditionState = ModelConditionState.Parse(parser);
                    x.ConditionStates.Add(conditionState);
                    parser.Temp = conditionState;
                }
            },
            {
                "ModelConditionState",
                (parser, x) =>
                {
                    var conditionState = ModelConditionState.Parse(parser);
                    x.ConditionStates.Add(conditionState);
                    parser.Temp = conditionState;
                }
            },

            { "IgnoreConditionStates", (parser, x) => x.IgnoreConditionStates = parser.ParseEnumBitArray<ModelConditionFlag>() },
            { "AliasConditionState", (parser, x) => x.ParseAliasConditionState(parser) },
            { "TransitionState", (parser, x) => x.TransitionStates.Add(TransitionState.Parse(parser)) },
            { "OkToChangeModelColor", (parser, x) => x.OkToChangeModelColor = parser.ParseBoolean() },
            { "ReceivesDynamicLights", (parser, x) => x.ReceivesDynamicLights = parser.ParseBoolean() },
            { "ProjectileBoneFeedbackEnabledSlots", (parser, x) => x.ProjectileBoneFeedbackEnabledSlots = parser.ParseEnumBitArray<WeaponSlot>() },
            { "AnimationsRequirePower", (parser, x) => x.AnimationsRequirePower = parser.ParseBoolean() },
            { "ParticlesAttachedToAnimatedBones", (parser, x) => x.ParticlesAttachedToAnimatedBones = parser.ParseBoolean() },
            { "MinLODRequired", (parser, x) => x.MinLodRequired = parser.ParseEnum<ModelLevelOfDetail>() },
            { "ExtraPublicBone", (parser, x) => x.ExtraPublicBones.Add(parser.ParseBoneName()) },
            { "AttachToBoneInAnotherModule", (parser, x) => x.AttachToBoneInAnotherModule = parser.ParseBoneName() },
            { "TrackMarks", (parser, x) => x.TrackMarks = parser.ParseFileName() },
            { "TrackMarksLeftBone", (parser, x) => x.TrackMarksLeftBone = parser.ParseAssetReference() },
            { "TrackMarksRightBone", (parser, x) => x.TrackMarksRightBone = parser.ParseAssetReference() },
            { "InitialRecoilSpeed", (parser, x) => x.InitialRecoilSpeed = parser.ParseFloat() },
            { "MaxRecoilDistance", (parser, x) => x.MaxRecoilDistance = parser.ParseFloat() },
            { "RecoilSettleSpeed", (parser, x) => x.RecoilSettleSpeed = parser.ParseFloat() },

            { "IdleAnimationState", (parser, x) => x.IdleAnimationState = AnimationState.Parse(parser) },
            { "AnimationState", (parser, x) => x.AnimationStates.Add(AnimationState.Parse(parser)) },
        };

        public BitArray<ModelConditionFlag> IgnoreConditionStates { get; private set; }
        public ModelConditionState DefaultConditionState { get; private set; }
        public List<ModelConditionState> ConditionStates { get; } = new List<ModelConditionState>();
        public List<TransitionState> TransitionStates { get; } = new List<TransitionState>();

        public bool OkToChangeModelColor { get; private set; }

        [AddedIn(SageGame.CncGeneralsZeroHour)]
        public bool ReceivesDynamicLights { get; private set; }

        public BitArray<WeaponSlot> ProjectileBoneFeedbackEnabledSlots { get; private set; }
        public bool AnimationsRequirePower { get; private set; }

        [AddedIn(SageGame.CncGeneralsZeroHour)]
        public bool ParticlesAttachedToAnimatedBones { get; private set; }

        /// <summary>
        /// Minimum level of detail required before this object appears in the game.
        /// </summary>
        public ModelLevelOfDetail MinLodRequired { get; private set; }

        public List<string> ExtraPublicBones { get; } = new List<string>();
        public string AttachToBoneInAnotherModule { get; private set; }

        public string TrackMarks { get; private set; }

        [AddedIn(SageGame.Bfme)]
        public string TrackMarksLeftBone { get; private set; }
        [AddedIn(SageGame.Bfme)]
        public string TrackMarksRightBone { get; private set; }

        public float InitialRecoilSpeed { get; private set; } = 2.0f;
        public float MaxRecoilDistance { get; private set; } = 3.0f;
        public float RecoilSettleSpeed { get; private set; } = 0.065f;

        public AnimationState IdleAnimationState { get; private set; }
        public List<AnimationState> AnimationStates { get; } = new List<AnimationState>();

        private void ParseAliasConditionState(IniParser parser)
        {
            if (!(parser.Temp is ModelConditionState lastConditionState))
            {
                throw new IniParseException("Cannot use AliasConditionState if there are no preceding ConditionStates", parser.CurrentPosition);
            }

            var conditionFlags = parser.ParseEnumBitArray<ModelConditionFlag>();

            var aliasedConditionState = lastConditionState.Clone(conditionFlags);

            ConditionStates.Add(aliasedConditionState);
        }

        internal override DrawModule CreateDrawModule(GameContext context)
        {
            return new W3dModelDraw(this, context);
        }
    }

    public enum ModelLevelOfDetail
    {
        [IniEnum("LOW")]
        Low,

        [IniEnum("MEDIUM")]
        Medium,

        [IniEnum("HIGH")]
        High,
    }
}
