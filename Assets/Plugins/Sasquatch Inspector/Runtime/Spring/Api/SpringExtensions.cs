using System.Collections.Generic;
using System.Threading.Tasks;
#if SASQ_TMP
using TMPro;
#endif
using UnityEngine;
#if SASQ_URP
using UnityEngine.Rendering.Universal;
#endif
#if SASQ_UGUI
using UnityEngine.UI;
#endif

namespace SasquatchSpring
{
    /// <summary>
    /// Fluent one-line spring animations for Unity's common component types —
    /// <c>transform.MoveTo(...)</c>, <c>image.FadeTo(...)</c>, and so on.
    /// <para>
    /// Every method returns a <see cref="Task"/>, so the motion can be <c>await</c>ed to completion.
    /// </para>
    /// </summary>
    public static class SpringExtensions
    {
        #region TRANSFORM (Move)

        /// <summary>
        /// Springs the Transform's world position to the target vector using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to move.</param>
        /// <param name="target">The target world position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveTo(this Transform t, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "Pos");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleVector3(() => tr.position, x => tr.position = x));

            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's world position to the target vector using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to move.</param>
        /// <param name="target">The target world position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveTo(this Transform t, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.MoveTo(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's X position only using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to move.</param>
        /// <param name="target">The target X position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveX(this Transform t, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "PosX");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleFloat(() => tr.position.x, x => {
                             var p = tr.position;
                             p.x = x;
                             tr.position = p;
                         }));

            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's X position only using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to move.</param>
        /// <param name="target">The target X position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveX(this Transform t, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.MoveX(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's Y position only using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to move.</param>
        /// <param name="target">The target Y position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveY(this Transform t, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "PosY");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleFloat(() => tr.position.y, y => {
                             var p = tr.position;
                             p.y = y;
                             tr.position = p;
                         }));

            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's Y position only using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to move.</param>
        /// <param name="target">The target Y position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveY(this Transform t, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.MoveY(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        #endregion

        #region TRANSFORM (Rotate)

        /// <summary>
        /// Rotates the Transform by adding the delta to the current target rotation.
        /// <para>Adds to the spring's CURRENT target, so rapid repeated calls stack onto the running
        /// animation instead of restarting from the live angle.</para>
        /// </summary>
        /// <param name="t">The transform to rotate.</param>
        /// <param name="delta">The amount to add to the rotation (Euler angles).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateBy(this Transform t, Vector3 delta, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var handle = agent.TryGetHandle<MotionHandleVector3>(WorldEulerKey(t))
                         ?? agent.GetHandle(WorldEulerKey(t), t, static tr => new MotionHandleVector3(() => tr.eulerAngles, x => tr.eulerAngles = x));

            Vector3 destination;
            if (handle.ID_X != -1)
            {
                Vector3 currentTarget = new Vector3(
                    SpringEngine.Instance.Targets[handle.ID_X],
                    SpringEngine.Instance.Targets[handle.ID_Y],
                    SpringEngine.Instance.Targets[handle.ID_Z]
                );
                destination = currentTarget + delta;
            }
            else
            {
                destination = t.eulerAngles + delta;
            }

            handle.To(destination, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's rotation (Euler angles) to the target.
        /// <para><i>Note: Uses linear interpolation on angles. Be wary of Gimbal lock for complex rotations.</i></para>
        /// </summary>
        /// <param name="t">The transform to rotate.</param>
        /// <param name="target">The target rotation (Euler angles).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateTo(this Transform t, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var handle = agent.TryGetHandle<MotionHandleVector3>(WorldEulerKey(t))
                         ?? agent.GetHandle(WorldEulerKey(t), t, static tr => new MotionHandleVector3(() => tr.eulerAngles, x => tr.eulerAngles = x));

            Vector3 current = t.eulerAngles;
            if (handle.ID_X != -1)
            {
                current.x = SpringEngine.Instance.Targets[handle.ID_X];
                current.y = SpringEngine.Instance.Targets[handle.ID_Y];
                current.z = SpringEngine.Instance.Targets[handle.ID_Z];
            }

            float dx = Mathf.DeltaAngle(current.x, target.x);
            float dy = Mathf.DeltaAngle(current.y, target.y);
            float dz = Mathf.DeltaAngle(current.z, target.z);

            handle.To(current + new Vector3(dx, dy, dz), config);

            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's rotation (Euler angles) to the target using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to rotate.</param>
        /// <param name="target">The target rotation (Euler angles).</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateTo(this Transform t, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.RotateTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion

        #region TRANSFORM (Scale)

        /// <summary>
        /// Springs the Transform's local scale to the target vector using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target local scale vector.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleTo(this Transform t, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "Scale");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleVector3(() => tr.localScale, x => tr.localScale = x));

            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's local scale to the target vector using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target local scale vector.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleTo(this Transform t, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.ScaleTo(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's local scale uniformly (all axes) using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniform">The target uniform scale value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleTo(this Transform t, float uniform, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.ScaleTo(Vector3.one * uniform, config, awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's local scale uniformly (all axes) using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniform">The target uniform scale value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleTo(this Transform t, float uniform, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.ScaleTo(Vector3.one * uniform, new SpringConfig(duration, bounce), awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's X scale only using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target X scale.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleX(this Transform t, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "ScaleX");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleFloat(() => tr.localScale.x, x => {
                             var s = tr.localScale;
                             s.x = x;
                             tr.localScale = s;
                         }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's X scale only using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target X scale.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleX(this Transform t, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleX(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the Transform's Y scale only using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target Y scale.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleY(this Transform t, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "ScaleY");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleFloat(() => tr.localScale.y, y => {
                             var s = tr.localScale;
                             s.y = y;
                             tr.localScale = s;
                         }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's Y scale only using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target Y scale.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleY(this Transform t, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleY(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the Transform's Z scale only using a specific configuration.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target Z scale.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleZ(this Transform t, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "ScaleZ");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleFloat(() => tr.localScale.z, z => {
                             var s = tr.localScale;
                             s.z = z;
                             tr.localScale = s;
                         }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Transform's Z scale only using duration and bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="target">The target Z scale.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleZ(this Transform t, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleZ(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion

        #region TRANSFORM (PUNCH Scale)

        /// <summary>
        /// Applies a temporary "punch" force to the Transform's scale, causing it to wobble and return to its original scale.
        /// </summary>
        /// <param name="t">The transform to punch.</param>
        /// <param name="punchDelta">The direction and magnitude of the punch velocity.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PunchScale(this Transform t, Vector3 punchDelta, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "Scale");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleVector3(() => tr.localScale, x => tr.localScale = x));

            float omega = 2f * Mathf.PI * config.Frequency;
            Vector3 velocity = punchDelta * (omega * 2.718f);

            handle.Punch(velocity, config);

            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Applies a temporary "punch" force to the Transform's scale, causing it to wobble and return to its original scale.
        /// </summary>
        /// <param name="t">The transform to punch.</param>
        /// <param name="punchDelta">The direction and magnitude of the punch velocity.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PunchScale(this Transform t, Vector3 punchDelta, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.PunchScale(punchDelta, new SpringConfig(duration, bounce), awaitStrategy);
        }

        #endregion

        #region TRANSFORM - Scale/Rotate FROM (makes use of springPivot)

        /// <summary>
        /// Scales the object towards a target size, anchored to a specific standard pivot point 
        /// (e.g., Feet/Bottom) without needing a parent container.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="targetScale">The target scale vector.</param>
        /// <param name="pivot">The pivot point enumeration (e.g., Bottom, Center, TopLeft).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, Vector3 targetScale, SpringPivot pivot, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return PerformScaleFrom(t, targetScale, pivot, config, awaitStrategy, null);
        }

        /// <summary>
        /// Scales the object towards a uniform target size, anchored to a specific standard pivot point.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniformScale">The target uniform scale value.</param>
        /// <param name="pivot">The pivot point enumeration (e.g., Bottom, Center, TopLeft).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, float uniformScale, SpringPivot pivot, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return PerformScaleFrom(t, Vector3.one * uniformScale, pivot, config, awaitStrategy, null);
        }

        /// <summary>
        /// Scales the object towards a target size, anchored to a specific standard pivot point using duration/bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="targetScale">The target scale vector.</param>
        /// <param name="pivot">The pivot point enumeration (e.g., Bottom, Center, TopLeft).</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, Vector3 targetScale, SpringPivot pivot, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleFrom(targetScale, pivot, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Scales the object towards a uniform target size, anchored to a specific standard pivot point using duration/bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniformScale">The target uniform scale value.</param>
        /// <param name="pivot">The pivot point enumeration (e.g., Bottom, Center, TopLeft).</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, float uniformScale, SpringPivot pivot, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleFrom(uniformScale, pivot, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Scales the object anchored to a specific LOCAL position offset.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="targetScale">The target scale vector.</param>
        /// <param name="localPivotOffset">The pivot point in local space (e.g. Vector3.zero is center).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, Vector3 targetScale, Vector3 localPivotOffset, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return PerformScaleFrom(t, targetScale, SpringPivot.Custom, config, awaitStrategy, localPivotOffset);
        }

        /// <summary>
        /// Scales the object uniformly anchored to a specific LOCAL position offset.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniformScale">The target uniform scale value.</param>
        /// <param name="localPivotOffset">The pivot point in local space (e.g. Vector3.zero is center).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, float uniformScale, Vector3 localPivotOffset, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return PerformScaleFrom(t, Vector3.one * uniformScale, SpringPivot.Custom, config, awaitStrategy, localPivotOffset);
        }

        /// <summary>
        /// Scales the object anchored to a specific LOCAL position offset using duration/bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="targetScale">The target scale vector.</param>
        /// <param name="localPivotOffset">The pivot point in local space.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, Vector3 targetScale, Vector3 localPivotOffset, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleFrom(targetScale, localPivotOffset, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Scales the object uniformly anchored to a specific LOCAL position offset using duration/bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniformScale">The target uniform scale value.</param>
        /// <param name="localPivotOffset">The pivot point in local space.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, float uniformScale, Vector3 localPivotOffset, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleFrom(uniformScale, localPivotOffset, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Scales the object using another Transform's position as the pivot anchor.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="targetScale">The target scale vector.</param>
        /// <param name="pivotAnchor">The transform to use as the pivot point.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, Vector3 targetScale, Transform pivotAnchor, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (pivotAnchor == null)
            {
                return PerformScaleFrom(t, targetScale, SpringPivot.Center, config, awaitStrategy, null);
            }

            Vector3 localOffset = t.InverseTransformPoint(pivotAnchor.position);
            return PerformScaleFrom(t, targetScale, SpringPivot.Custom, config, awaitStrategy, localOffset);
        }

        /// <summary>
        /// Scales the object uniformly using another Transform's position as the pivot anchor.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniformScale">The target uniform scale value.</param>
        /// <param name="pivotAnchor">The transform to use as the pivot point.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, float uniformScale, Transform pivotAnchor, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.ScaleFrom(Vector3.one * uniformScale, pivotAnchor, config, awaitStrategy);
        }

        /// <summary>
        /// Scales the object using another Transform's position as the pivot anchor using duration/bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="targetScale">The target scale vector.</param>
        /// <param name="pivotAnchor">The transform to use as the pivot point.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, Vector3 targetScale, Transform pivotAnchor, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleFrom(targetScale, pivotAnchor, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Scales the object uniformly using another Transform's position as the pivot anchor using duration/bounce parameters.
        /// </summary>
        /// <param name="t">The transform to scale.</param>
        /// <param name="uniformScale">The target uniform scale value.</param>
        /// <param name="pivotAnchor">The transform to use as the pivot point.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ScaleFrom(this Transform t, float uniformScale, Transform pivotAnchor, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            t.ScaleFrom(uniformScale, pivotAnchor, new SpringConfig(duration, bounce), awaitStrategy);

        private static Task PerformScaleFrom(Transform t, Vector3 targetScale, SpringPivot pivot, SpringConfig config, AwaitStrategy awaitStrategy, Vector3? customOffset = null)
        {
            Quaternion currentRot = t.rotation;
            t.rotation = Quaternion.identity;

            Vector3 pivotOffsetLocal = SpringPivotUtils.Resolve(t, pivot, customOffset);

            t.rotation = currentRot;

            Vector3 anchorPointWorld = t.TransformPoint(pivotOffsetLocal);
            Vector3 targetPosition = anchorPointWorld - (t.rotation * Vector3.Scale(targetScale, pivotOffsetLocal));

            var agent = GetAgent(t);
            var scaleKey = new MotionKey(t, "Scale");
            var scaleHandle = agent.TryGetHandle<MotionHandleVector3>(scaleKey)
                              ?? agent.GetHandle(scaleKey, t, static tr => new MotionHandleVector3(() => tr.localScale, x => tr.localScale = x));

            // No UpdateSetter call here: the cached handle is keyed by (this transform, "Scale"), so its
            // setter already writes this transform's localScale — supplying another just allocates a closure.
            scaleHandle.To(targetScale, config);

            return t.MoveTo(targetPosition, config, awaitStrategy);
        }

        #endregion

        #region RECT TRANSFORM

        /// <summary>
        /// Springs the RectTransform's anchored position (X/Y) to the target.
        /// </summary>
        /// <param name="rect">The RectTransform to move.</param>
        /// <param name="target">The target anchored position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task AnchorTo(this RectTransform rect, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rect.transform);
            var key = new MotionKey(rect, "Anchor");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, rect, static r => new MotionHandleVector3(() => r.anchoredPosition3D, x => r.anchoredPosition3D = x));

            handle.To(new Vector3(target.x, target.y, rect.anchoredPosition3D.z), config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the RectTransform's anchored position (X/Y) to the target using duration and bounce parameters.
        /// </summary>
        /// <param name="rect">The RectTransform to move.</param>
        /// <param name="target">The target anchored position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task AnchorTo(this RectTransform rect, Vector2 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return rect.AnchorTo(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        #endregion

#if SASQ_AUDIO // AudioSource lives in com.unity.modules.audio, a REMOVABLE built-in module.
        #region AUDIO

        /// <summary>
        /// Springs the AudioSource's volume to the target level.
        /// </summary>
        /// <param name="source">The AudioSource to modify.</param>
        /// <param name="target">The target volume level (0.0 to 1.0).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task VolumeTo(this AudioSource source, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(source.transform);
            var key = new MotionKey(source, "Volume");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, source, static s => new MotionHandleFloat(() => s.volume, x => s.volume = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the AudioSource's volume to the target level using duration and bounce parameters.
        /// </summary>
        /// <param name="source">The AudioSource to modify.</param>
        /// <param name="target">The target volume level (0.0 to 1.0).</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task VolumeTo(this AudioSource source, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            source.VolumeTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the AudioSource's pitch to the target level.
        /// </summary>
        /// <param name="source">The AudioSource to modify.</param>
        /// <param name="target">The target pitch level.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PitchTo(this AudioSource source, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(source.transform);
            var key = new MotionKey(source, "Pitch");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, source, static s => new MotionHandleFloat(() => s.pitch, x => s.pitch = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the AudioSource's pitch to the target level using duration and bounce parameters.
        /// </summary>
        /// <param name="source">The AudioSource to modify.</param>
        /// <param name="target">The target pitch level.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PitchTo(this AudioSource source, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            source.PitchTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion
#endif // SASQ_AUDIO

        #region CAMERA

        /// <summary>
        /// Springs the Camera's Field of View (FOV).
        /// </summary>
        /// <param name="cam">The Camera to modify.</param>
        /// <param name="target">The target FOV value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FieldOfViewTo(this Camera cam, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(cam.transform);
            var key = new MotionKey(cam, "FOV");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, cam, static c => new MotionHandleFloat(() => c.fieldOfView, x => c.fieldOfView = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Camera's Field of View (FOV) using duration and bounce parameters.
        /// </summary>
        /// <param name="cam">The Camera to modify.</param>
        /// <param name="target">The target FOV value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FieldOfViewTo(this Camera cam, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            cam.FieldOfViewTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the Camera's Orthographic Size.
        /// </summary>
        /// <param name="cam">The Camera to modify.</param>
        /// <param name="target">The target orthographic size.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task OrthographicSizeTo(this Camera cam, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(cam.transform);
            var key = new MotionKey(cam, "OrthoSize");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, cam, static c => new MotionHandleFloat(() => c.orthographicSize, x => c.orthographicSize = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Camera's Orthographic Size using duration and bounce parameters.
        /// </summary>
        /// <param name="cam">The Camera to modify.</param>
        /// <param name="target">The target orthographic size.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task OrthographicSizeTo(this Camera cam, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            cam.OrthographicSizeTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion

        #region LIGHT

        /// <summary>
        /// Springs the Light's intensity.
        /// </summary>
        /// <param name="light">The Light to modify.</param>
        /// <param name="target">The target intensity value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task IntensityTo(this Light light, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "Intensity");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleFloat(() => l.intensity, x => l.intensity = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Light's intensity using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light to modify.</param>
        /// <param name="target">The target intensity value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task IntensityTo(this Light light, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.IntensityTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the Light's color.
        /// </summary>
        /// <param name="light">The Light to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Light light, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleColor(() => l.color, x => l.color = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Light's color using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Light light, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.ColorTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the Light's range.
        /// </summary>
        /// <param name="light">The Light to modify.</param>
        /// <param name="target">The target range value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RangeTo(this Light light, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "Range");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleFloat(() => l.range, x => l.range = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the Light's range using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light to modify.</param>
        /// <param name="target">The target range value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RangeTo(this Light light, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.RangeTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion

    #if SASQ_URP
        #region LIGHT 2D (URP)

        /// <summary>
        /// Springs the URP 2D Light's color.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Light2D light, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleColor(() => l.color, x => l.color = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the URP 2D Light's color using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Light2D light, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.ColorTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the URP 2D Light's intensity.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target intensity value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task IntensityTo(this Light2D light, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "Intensity");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleFloat(() => l.intensity, x => l.intensity = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the URP 2D Light's intensity using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target intensity value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task IntensityTo(this Light2D light, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.IntensityTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the URP 2D Point Light's inner radius.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target inner radius value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PointLightInnerRadiusTo(this Light2D light, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "InnerRad");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleFloat(() => l.pointLightInnerRadius, x => l.pointLightInnerRadius = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the URP 2D Point Light's inner radius using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target inner radius value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PointLightInnerRadiusTo(this Light2D light, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.PointLightInnerRadiusTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs the URP 2D Point Light's outer radius.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target outer radius value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PointLightOuterRadiusTo(this Light2D light, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(light.transform);
            var key = new MotionKey(light, "OuterRad");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, light, static l => new MotionHandleFloat(() => l.pointLightOuterRadius, x => l.pointLightOuterRadius = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the URP 2D Point Light's outer radius using duration and bounce parameters.
        /// </summary>
        /// <param name="light">The Light2D to modify.</param>
        /// <param name="target">The target outer radius value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task PointLightOuterRadiusTo(this Light2D light, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            light.PointLightOuterRadiusTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion
    #endif

        #region SPRITE RENDERER

        /// <summary>
        /// Springs the SpriteRenderer's color.
        /// </summary>
        /// <param name="sr">The SpriteRenderer to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this SpriteRenderer sr, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(sr.transform);
            var key = new MotionKey(sr, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, sr, static s => new MotionHandleColor(() => s.color, x => s.color = x));

            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the SpriteRenderer's color using duration and bounce parameters.
        /// </summary>
        /// <param name="sr">The SpriteRenderer to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this SpriteRenderer sr, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return sr.ColorTo(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        /// <summary>
        /// Springs the SpriteRenderer's alpha (opacity).
        /// </summary>
        /// <param name="sr">The SpriteRenderer to modify.</param>
        /// <param name="alpha">The target alpha value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FadeTo(this SpriteRenderer sr, float alpha, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(sr.transform);
            var key = new MotionKey(sr, "Alpha");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, sr, static s => new MotionHandleFloat(() => s.color.a, a => {
                             var c = s.color;
                             c.a = a;
                             s.color = c;
                         }));
            handle.To(alpha, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs the SpriteRenderer's alpha (opacity) using duration and bounce parameters.
        /// </summary>
        /// <param name="sr">The SpriteRenderer to modify.</param>
        /// <param name="alpha">The target alpha value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FadeTo(this SpriteRenderer sr, float alpha, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            sr.FadeTo(alpha, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion

        #region MATERIAL

        // NOTE: the material's own identity is part of the handle key (MotionKey holds the Material
        // reference), so animating two different materials on one GameObject/Agent can't collide.
        //
        // MotionKey compares by REFERENCE identity rather than by an instance id, which is also what keeps
        // these springs compiling across editor lines: Object.GetInstanceID() is ERROR-level obsolete on
        // Unity 6000.5, and its replacement GetEntityId() does not exist on 6000.0 (the package floor).
        // Keying by reference needs neither.

        /// <summary>
        /// Springs a Color property on a Material using a Property ID.
        /// <para>Requires a context transform (usually the object with the Renderer) to attach the MotionAgent to.</para>
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID (use Shader.PropertyToID).</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Material mat, Transform context, int propertyID, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            var key = new MotionKey(mat, "MatColor", propertyID);

            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, (m: mat, id: propertyID),
                             static a => new MotionHandleColor(() => a.m.GetColor(a.id), x => a.m.SetColor(a.id, x)));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Color property on a Material using a Property Name.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property (e.g., "_Color").</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Material mat, Transform context, string propertyName, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.ColorTo(context, Shader.PropertyToID(propertyName), target, config, awaitStrategy);

        /// <summary>
        /// Springs a Color property on a Material using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property (e.g., "_Color").</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Material mat, Transform context, string propertyName, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.ColorTo(context, propertyName, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Float property on a Material using a Property Name.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FloatTo(this Material mat, Transform context, string propertyName, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            int id = Shader.PropertyToID(propertyName);
            var key = new MotionKey(mat, "MatFloat", id);

            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, (m: mat, id: id),
                             static a => new MotionHandleFloat(() => a.m.GetFloat(a.id), x => a.m.SetFloat(a.id, x)));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Float property on a Material using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FloatTo(this Material mat, Transform context, string propertyName, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.FloatTo(context, propertyName, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Float property on a Material using a Property ID.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FloatTo(this Material mat, Transform context, int propertyID, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            var key = new MotionKey(mat, "MatFloat", propertyID);

            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, (m: mat, id: propertyID), static a => new MotionHandleFloat(
                             () => a.m.GetFloat(a.id),
                             x => a.m.SetFloat(a.id, x)
                         ));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Float property on a Material using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FloatTo(this Material mat, Transform context, int propertyID, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.FloatTo(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector2 property on a Material using a Property Name.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector2To(this Material mat, Transform context, string propertyName, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            int id = Shader.PropertyToID(propertyName);
            var key = new MotionKey(mat, "MatVec2", id);

            // Every shader vector property is a float4. Read the live value and write back only x/y, or the
            // z and w this overload never claimed to touch (e.g. a _ST's offset half) get zeroed.
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, (m: mat, id: id), static a => new MotionHandleVector3(
                             () => { Vector4 v = a.m.GetVector(a.id); return new Vector3(v.x, v.y, 0f); },
                             x => { Vector4 v = a.m.GetVector(a.id); a.m.SetVector(a.id, new Vector4(x.x, x.y, v.z, v.w)); }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Vector2 property on a Material using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector2To(this Material mat, Transform context, string propertyName, Vector2 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.Vector2To(context, propertyName, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector2 property on a Material using a Property ID.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector2To(this Material mat, Transform context, int propertyID, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            var key = new MotionKey(mat, "MatVec2", propertyID);

            // float4 property — preserve z/w (see the Property Name overload).
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, (m: mat, id: propertyID), static a => new MotionHandleVector3(
                             () => { Vector4 v = a.m.GetVector(a.id); return new Vector3(v.x, v.y, 0f); },
                             x => { Vector4 v = a.m.GetVector(a.id); a.m.SetVector(a.id, new Vector4(x.x, x.y, v.z, v.w)); }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Vector2 property on a Material using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector2To(this Material mat, Transform context, int propertyID, Vector2 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.Vector2To(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector3 property on a Material using a Property Name.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector3To(this Material mat, Transform context, string propertyName, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            int id = Shader.PropertyToID(propertyName);
            var key = new MotionKey(mat, "MatVec3", id);

            // float4 property — preserve w, which a Vector3 spring never addresses.
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, (m: mat, id: id), static a => new MotionHandleVector3(
                             () => { Vector4 v = a.m.GetVector(a.id); return new Vector3(v.x, v.y, v.z); },
                             x => { Vector4 v = a.m.GetVector(a.id); a.m.SetVector(a.id, new Vector4(x.x, x.y, x.z, v.w)); }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Vector3 property on a Material using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyName">The name of the shader property.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector3To(this Material mat, Transform context, string propertyName, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.Vector3To(context, propertyName, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector3 property on a Material using a Property ID.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector3To(this Material mat, Transform context, int propertyID, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(context);
            var key = new MotionKey(mat, "MatVec3", propertyID);

            // float4 property — preserve w (see the Property Name overload).
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, (m: mat, id: propertyID), static a => new MotionHandleVector3(
                             () => { Vector4 v = a.m.GetVector(a.id); return new Vector3(v.x, v.y, v.z); },
                             x => { Vector4 v = a.m.GetVector(a.id); a.m.SetVector(a.id, new Vector4(x.x, x.y, x.z, v.w)); }));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Vector3 property on a Material using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="mat">The Material to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task Vector3To(this Material mat, Transform context, int propertyID, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            mat.Vector3To(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Float property on a list of Materials using a Property Name.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task FloatTo(this IList<Material> materials, Transform context, string property, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                {
                    tasks.Add(materials[i].FloatTo(context, property, target, config, awaitStrategy));
                }
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Float property on a list of Materials using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task FloatTo(this IList<Material> materials, Transform context, string property, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.FloatTo(context, property, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Float property on a list of Materials using a Property ID.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task FloatTo(this IList<Material> materials, Transform context, int propertyID, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    tasks.Add(materials[i].FloatTo(context, propertyID, target, config, awaitStrategy));
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Float property on a list of Materials using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target float value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task FloatTo(this IList<Material> materials, Transform context, int propertyID, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.FloatTo(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Color property on a list of Materials using a Property Name.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task ColorTo(this IList<Material> materials, Transform context, string property, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                {
                    tasks.Add(materials[i].ColorTo(context, property, target, config, awaitStrategy));
                }
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Color property on a list of Materials using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task ColorTo(this IList<Material> materials, Transform context, string property, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.ColorTo(context, property, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Color property on a list of Materials using a Property ID.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task ColorTo(this IList<Material> materials, Transform context, int propertyID, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    tasks.Add(materials[i].ColorTo(context, propertyID, target, config, awaitStrategy));
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Color property on a list of Materials using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task ColorTo(this IList<Material> materials, Transform context, int propertyID, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.ColorTo(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector2 property on a list of Materials using a Property Name.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector2To(this IList<Material> materials, Transform context, string property, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                {
                    tasks.Add(materials[i].Vector2To(context, property, target, config, awaitStrategy));
                }
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Vector2 property on a list of Materials using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector2To(this IList<Material> materials, Transform context, string property, Vector2 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.Vector2To(context, property, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector2 property on a list of Materials using a Property ID.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector2To(this IList<Material> materials, Transform context, int propertyID, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    tasks.Add(materials[i].Vector2To(context, propertyID, target, config, awaitStrategy));
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Vector2 property on a list of Materials using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector2 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector2To(this IList<Material> materials, Transform context, int propertyID, Vector2 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.Vector2To(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector3 property on a list of Materials using a Property Name.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector3To(this IList<Material> materials, Transform context, string property, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                {
                    tasks.Add(materials[i].Vector3To(context, property, target, config, awaitStrategy));
                }
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Vector3 property on a list of Materials using a Property Name with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="property">The name of the shader property.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector3To(this IList<Material> materials, Transform context, string property, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.Vector3To(context, property, target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Vector3 property on a list of Materials using a Property ID.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector3To(this IList<Material> materials, Transform context, int propertyID, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            if (materials == null || materials.Count == 0) return Task.CompletedTask;

            var tasks = new List<Task>(materials.Count);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    tasks.Add(materials[i].Vector3To(context, propertyID, target, config, awaitStrategy));
            }
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Springs a Vector3 property on a list of Materials using a Property ID with duration and bounce parameters.
        /// </summary>
        /// <param name="materials">The list of Materials to animate.</param>
        /// <param name="context">The GameObject/Transform that "owns" the update cycle.</param>
        /// <param name="propertyID">The Shader Property ID.</param>
        /// <param name="target">The target Vector3 value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when all springs in the list settle.</returns>
        public static Task Vector3To(this IList<Material> materials, Transform context, int propertyID, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            materials.Vector3To(context, propertyID, target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion

#if SASQ_PHYSICS // Rigidbody lives in com.unity.modules.physics, a REMOVABLE built-in module.
        #region RIGIDBODY

        /// <summary>
        /// Springs a Rigidbody to a position using <c>MovePosition</c>.
        /// <para><i>Safe for physics interactions.</i></para>
        /// </summary>
        /// <param name="rb">The Rigidbody to move.</param>
        /// <param name="target">The target world position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveTo(this Rigidbody rb, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rb.transform);
            // The write goes through SpringPhysicsQueue, which flushes in FixedUpdate: MovePosition is a
            // physics-step call, and the spring tick runs on the render clock. See SpringPhysicsQueue.
            var key = new MotionKey(rb, "RbPos");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, rb, static b => new MotionHandleVector3(
                             () => SpringPhysicsQueue.GetPosition(b), x => SpringPhysicsQueue.SetPosition(b, x)));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Rigidbody to a position using <c>MovePosition</c> with duration and bounce parameters.
        /// </summary>
        /// <param name="rb">The Rigidbody to move.</param>
        /// <param name="target">The target world position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveTo(this Rigidbody rb, Vector3 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            rb.MoveTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Rigidbody to a rotation using <c>MoveRotation</c>.
        /// </summary>
        /// <param name="rb">The Rigidbody to rotate.</param>
        /// <param name="targetEuler">The target rotation (Euler angles).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateTo(this Rigidbody rb, Vector3 targetEuler, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rb.transform);
            // Quaternion-backed, so it takes the SHORTEST ARC. Springing rb.rotation.eulerAngles as a Vector3
            // instead would go the long way round: Unity normalises those to [0,360), so a 350 -> 10 degree
            // turn reads as 340 degrees backwards. (The Transform Euler path guards the same wrap with
            // Mathf.DeltaAngle.) Writes flush in FixedUpdate — see SpringPhysicsQueue.
            var key = new MotionKey(rb, "RbRot");
            var handle = agent.TryGetHandle<MotionHandleQuaternion>(key)
                         ?? agent.GetHandle(key, rb, static b => new MotionHandleQuaternion(
                             () => SpringPhysicsQueue.GetRotation(b), x => SpringPhysicsQueue.SetRotation(b, x)));
            handle.To(Quaternion.Euler(targetEuler), config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Rigidbody to a rotation using <c>MoveRotation</c> with duration and bounce parameters.
        /// </summary>
        /// <param name="rb">The Rigidbody to rotate.</param>
        /// <param name="targetEuler">The target rotation (Euler angles).</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateTo(this Rigidbody rb, Vector3 targetEuler, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            rb.RotateTo(targetEuler, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion
#endif // SASQ_PHYSICS

#if SASQ_PHYSICS2D // Rigidbody2D lives in com.unity.modules.physics2d, a REMOVABLE built-in module.
        #region RIGIDBODY 2D

        /// <summary>
        /// Springs a Rigidbody2D to a position using <c>MovePosition</c>.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to move.</param>
        /// <param name="target">The target position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveTo(this Rigidbody2D rb, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rb.transform);
            var key = new MotionKey(rb, "Rb2DPos");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, rb, static b => new MotionHandleVector3(
                             () => SpringPhysicsQueue.GetPosition(b), x => SpringPhysicsQueue.SetPosition(b, x)));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Rigidbody2D to a position using <c>MovePosition</c> with duration and bounce parameters.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to move.</param>
        /// <param name="target">The target position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveTo(this Rigidbody2D rb, Vector2 target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            rb.MoveTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Rigidbody2D's X position.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to move.</param>
        /// <param name="target">The target X position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveX(this Rigidbody2D rb, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rb.transform);
            var key = new MotionKey(rb, "Rb2DPosX");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, rb, static b => new MotionHandleFloat(
                             () => SpringPhysicsQueue.GetPosition(b).x,
                             x => SpringPhysicsQueue.SetPosition(b, new Vector2(x, SpringPhysicsQueue.GetPosition(b).y))));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Rigidbody2D's X position with duration and bounce parameters.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to move.</param>
        /// <param name="target">The target X position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveX(this Rigidbody2D rb, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            rb.MoveX(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Rigidbody2D's Y position.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to move.</param>
        /// <param name="target">The target Y position.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveY(this Rigidbody2D rb, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rb.transform);
            var key = new MotionKey(rb, "Rb2DPosY");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, rb, static b => new MotionHandleFloat(
                             () => SpringPhysicsQueue.GetPosition(b).y,
                             y => SpringPhysicsQueue.SetPosition(b, new Vector2(SpringPhysicsQueue.GetPosition(b).x, y))));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Rigidbody2D's Y position with duration and bounce parameters.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to move.</param>
        /// <param name="target">The target Y position.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task MoveY(this Rigidbody2D rb, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            rb.MoveY(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Rigidbody2D's rotation.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to rotate.</param>
        /// <param name="targetAngle">The target angle.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateTo(this Rigidbody2D rb, float targetAngle, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rb.transform);
            // Rigidbody2D.rotation is a free-running float (it can exceed 360), so springing it directly is
            // the correct absolute-angle behaviour — unlike the 3D case, there is no [0,360) wrap to cross.
            var key = new MotionKey(rb, "Rb2DRot");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, rb, static b => new MotionHandleFloat(
                             () => SpringPhysicsQueue.GetRotation(b), x => SpringPhysicsQueue.SetRotation(b, x)));
            handle.To(targetAngle, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Rigidbody2D's rotation with duration and bounce parameters.
        /// </summary>
        /// <param name="rb">The Rigidbody2D to rotate.</param>
        /// <param name="targetAngle">The target angle.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task RotateTo(this Rigidbody2D rb, float targetAngle, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            rb.RotateTo(targetAngle, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion
#endif // SASQ_PHYSICS2D

        #region UI

#if SASQ_UGUI // uGUI (Image / Slider / Graphic). com.unity.ugui is optional; these compile out when it's absent.

        /// <summary>
        /// Springs an Image's color.
        /// </summary>
        /// <param name="img">The Image to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Image img, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(img.transform);
            var key = new MotionKey(img, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, img, static i => new MotionHandleColor(() => i.color, x => i.color = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs an Image's color using duration and bounce parameters.
        /// </summary>
        /// <param name="img">The Image to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Image img, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            img.ColorTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs an Image's alpha (fade).
        /// </summary>
        /// <param name="img">The Image to modify.</param>
        /// <param name="alpha">The target alpha value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FadeTo(this Image img, float alpha, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(img.transform);
            var key = new MotionKey(img, "Alpha");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, img, static i => new MotionHandleFloat(() => i.color.a, a => {
                             var c = i.color;
                             c.a = a;
                             i.color = c;
                         }));
            handle.To(alpha, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs an Image's alpha (fade) using duration and bounce parameters.
        /// </summary>
        /// <param name="img">The Image to modify.</param>
        /// <param name="alpha">The target alpha value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FadeTo(this Image img, float alpha, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            img.FadeTo(alpha, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs an Image's Fill Amount.
        /// </summary>
        /// <param name="img">The Image to modify.</param>
        /// <param name="target">The target fill amount (0.0 to 1.0).</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FillAmountTo(this Image img, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(img.transform);
            var key = new MotionKey(img, "Fill");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, img, static i => new MotionHandleFloat(() => i.fillAmount, x => i.fillAmount = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs an Image's Fill Amount using duration and bounce parameters.
        /// </summary>
        /// <param name="img">The Image to modify.</param>
        /// <param name="target">The target fill amount (0.0 to 1.0).</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FillAmountTo(this Image img, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            img.FillAmountTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a Slider's value.
        /// </summary>
        /// <param name="slider">The Slider to modify.</param>
        /// <param name="target">The target value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ValueTo(this Slider slider, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(slider.transform);
            var key = new MotionKey(slider, "Value");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, slider, static s => new MotionHandleFloat(() => s.value, x => s.value = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a Slider's value using duration and bounce parameters.
        /// </summary>
        /// <param name="slider">The Slider to modify.</param>
        /// <param name="target">The target value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ValueTo(this Slider slider, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            slider.ValueTo(target, new SpringConfig(duration, bounce), awaitStrategy);

#endif // SASQ_UGUI  (CanvasGroup below is a built-in module, not uGUI — stays ungated.)

        /// <summary>
        /// Springs a CanvasGroup's alpha.
        /// </summary>
        /// <param name="cg">The CanvasGroup to modify.</param>
        /// <param name="alpha">The target alpha value.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FadeTo(this CanvasGroup cg, float alpha, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(cg.transform);
            var key = new MotionKey(cg, "Alpha");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, cg, static c => new MotionHandleFloat(() => c.alpha, x => c.alpha = x));

            handle.To(alpha, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a CanvasGroup's alpha using duration and bounce parameters.
        /// </summary>
        /// <param name="cg">The CanvasGroup to modify.</param>
        /// <param name="alpha">The target alpha value.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FadeTo(this CanvasGroup cg, float alpha, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return cg.FadeTo(alpha, new SpringConfig(duration, bounce), awaitStrategy);
        }

#if SASQ_UGUI

        /// <summary>
        /// Springs a general UI Graphic's color (Text, Image, RawImage, etc.).
        /// </summary>
        /// <param name="graphic">The Graphic to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Graphic graphic, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(graphic.transform);
            var key = new MotionKey(graphic, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, graphic, static g => new MotionHandleColor(() => g.color, x => g.color = x));

            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a general UI Graphic's color (Text, Image, RawImage, etc.) using duration and bounce parameters.
        /// </summary>
        /// <param name="graphic">The Graphic to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this Graphic graphic, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return graphic.ColorTo(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

#endif // SASQ_UGUI

        #endregion

    #if SASQ_TMP
        #region TEXT MESH PRO

        /// <summary>
        /// Springs a TextMeshPro's font size.
        /// </summary>
        /// <param name="tmp">The TMP_Text to modify.</param>
        /// <param name="target">The target font size.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FontSizeTo(this TMP_Text tmp, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(tmp.transform);
            var key = new MotionKey(tmp, "FontSize");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, tmp, static m => new MotionHandleFloat(() => m.fontSize, x => m.fontSize = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a TextMeshPro's font size using duration and bounce parameters.
        /// </summary>
        /// <param name="tmp">The TMP_Text to modify.</param>
        /// <param name="target">The target font size.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task FontSizeTo(this TMP_Text tmp, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            tmp.FontSizeTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a TextMeshPro's color.
        /// </summary>
        /// <param name="tmp">The TMP_Text to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this TMP_Text tmp, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(tmp.transform);
            var key = new MotionKey(tmp, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, tmp, static m => new MotionHandleColor(() => m.color, x => m.color = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a TextMeshPro's color using duration and bounce parameters.
        /// </summary>
        /// <param name="tmp">The TMP_Text to modify.</param>
        /// <param name="target">The target Color.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task ColorTo(this TMP_Text tmp, Color target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            tmp.ColorTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        /// <summary>
        /// Springs a TextMeshPro's character spacing.
        /// </summary>
        /// <param name="tmp">The TMP_Text to modify.</param>
        /// <param name="target">The target character spacing.</param>
        /// <param name="config">The spring feel: settle duration + bounce, or a <c>SpringConfig</c> preset.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task CharacterSpacingTo(this TMP_Text tmp, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(tmp.transform);
            var key = new MotionKey(tmp, "CharSpacing");
            var handle = agent.TryGetHandle<MotionHandleFloat>(key)
                         ?? agent.GetHandle(key, tmp, static m => new MotionHandleFloat(() => m.characterSpacing, x => m.characterSpacing = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>
        /// Springs a TextMeshPro's character spacing using duration and bounce parameters.
        /// </summary>
        /// <param name="tmp">The TMP_Text to modify.</param>
        /// <param name="target">The target character spacing.</param>
        /// <param name="duration">How long the motion should take to settle (in seconds).</param>
        /// <param name="bounce">Overshoot: 0 = none, 1 = very elastic, up to 2 for extra wobble.</param>
        /// <param name="awaitStrategy">The strategy used to determine when the Task completes.</param>
        /// <returns>A Task that completes when the spring settles.</returns>
        public static Task CharacterSpacingTo(this TMP_Text tmp, float target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling) =>
            tmp.CharacterSpacingTo(target, new SpringConfig(duration, bounce), awaitStrategy);

        #endregion
    #endif

        #region QUATERNION ROTATION (gimbal-lock-free)

        /// <summary>
        /// Springs the Transform's world rotation to a target Quaternion along the shortest arc.
        /// Unlike the Euler <see cref="Spring.Rotate"/> path this is gimbal-lock-free, and it can
        /// overshoot slightly past the target before easing back for a natural rotational bounce.
        /// </summary>
        public static Task RotateTo(this Transform t, Quaternion target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(t);
            var key = new MotionKey(t, "RotQ");
            var handle = agent.TryGetHandle<MotionHandleQuaternion>(key)
                         ?? agent.GetHandle(key, t, static tr => new MotionHandleQuaternion(() => tr.rotation, x => tr.rotation = x));
            handle.To(target, config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>Quaternion rotation spring with duration/bounce parameters (shortest-arc, gimbal-free).</summary>
        public static Task RotateTo(this Transform t, Quaternion target, float duration, float bounce = 0.5f, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            return t.RotateTo(target, new SpringConfig(duration, bounce), awaitStrategy);
        }

        #endregion

        #region FROM HELPERS & RECT SIZE
        // "From" helpers snap the property to an explicit start, then spring to the target — handy for
        // entrances (fade in from 0, slide in from off-screen) without a separate snap call.

        /// <summary>Snaps world position to <paramref name="from"/>, then springs to <paramref name="target"/>.</summary>
        public static Task MoveFrom(this Transform t, Vector3 from, Vector3 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            t.position = from;
            return t.MoveTo(target, config, awaitStrategy);
        }

        /// <summary>Snaps local scale to a uniform <paramref name="from"/>, then springs to a uniform <paramref name="target"/>.</summary>
        public static Task ScaleFromTo(this Transform t, float from, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            t.localScale = Vector3.one * from;
            return t.ScaleTo(target, config, awaitStrategy);
        }

        /// <summary>Snaps the CanvasGroup alpha to <paramref name="from"/>, then springs to <paramref name="target"/> (fade in/out).</summary>
        public static Task FadeFrom(this CanvasGroup cg, float from, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            cg.alpha = from;
            return cg.FadeTo(target, config, awaitStrategy);
        }

        /// <summary>Snaps the SpriteRenderer alpha to <paramref name="from"/>, then springs to <paramref name="target"/>.</summary>
        public static Task FadeFrom(this SpriteRenderer sr, float from, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var c = sr.color; c.a = from; sr.color = c;
            return sr.FadeTo(target, config, awaitStrategy);
        }

#if SASQ_UGUI
        /// <summary>Snaps the Image alpha to <paramref name="from"/>, then springs to <paramref name="target"/>.</summary>
        public static Task FadeFrom(this Image img, float from, float target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var c = img.color; c.a = from; img.color = c;
            return img.FadeTo(target, config, awaitStrategy);
        }

        /// <summary>Snaps a Graphic's color to <paramref name="from"/>, then springs to <paramref name="target"/>.</summary>
        public static Task ColorFrom(this Graphic graphic, Color from, Color target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            graphic.color = from;
            return graphic.ColorTo(target, config, awaitStrategy);
        }
#endif // SASQ_UGUI

        /// <summary>Springs the RectTransform's sizeDelta (width/height) to the target.</summary>
        public static Task SizeDeltaTo(this RectTransform rect, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            var agent = GetAgent(rect.transform);
            var key = new MotionKey(rect, "SizeDelta");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, rect, static r => new MotionHandleVector3(() => r.sizeDelta, x => r.sizeDelta = new Vector2(x.x, x.y)));
            handle.To(new Vector3(target.x, target.y, 0f), config);
            return handle.WaitFor(awaitStrategy);
        }

        /// <summary>Snaps anchored position to <paramref name="from"/>, then springs to <paramref name="target"/>.</summary>
        public static Task AnchorFrom(this RectTransform rect, Vector2 from, Vector2 target, SpringConfig config, AwaitStrategy awaitStrategy = AwaitStrategy.PhysicalSettling)
        {
            rect.anchoredPosition = from;
            return rect.AnchorTo(target, config, awaitStrategy);
        }

        #endregion

        #region HELPERS

        /// <summary>
        /// The WORLD-Euler rotation slot, shared by <c>RotateTo(Vector3)</c> and <c>RotateBy</c>.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT the same slot <see cref="Spring.Rotate"/> uses — that one drives LOCAL Euler
        /// (<see cref="SpringKeys.LocalEuler"/>). Both produce a MotionHandleVector3, so a single shared "Rot"
        /// key would let whichever API is called first own the rotation space for the object's lifetime: a
        /// later world-space RotateTo would write world angles into localEulerAngles and compute its
        /// shortest-arc delta against the wrong basis. Same split as Spring.Move/MoveLocal's "Pos"/"LocalPos".
        /// </remarks>
        internal static MotionKey WorldEulerKey(Transform t) => new MotionKey(t, SpringKeys.WorldEuler);

        private static MotionAgent GetAgent(Transform t)
        {
            if (!t.TryGetComponent(out MotionAgent agent))
            {
                agent = t.gameObject.AddComponent<MotionAgent>();
                // HideInInspector alone would let an edit-time spring BAKE this auto-added component into the
                // user's scene/prefab — invisible in the Inspector, and shipped in their build. DontSave keeps
                // it runtime-only. (Not HideAndDontSave: that bundles NotEditable, which we don't want.)
                agent.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            }
            return agent;
        }

        /// <summary>
        /// Forces the Spring Engine to adopt the object's current state immediately.
        /// Stops all active springs on this object.
        /// <para>Call this after manually teleporting an object to prevent the spring from pulling it back.</para>
        /// </summary>
        /// <param name="t">The transform to snap.</param>
        public static void Snap(this Transform t)
        {
            if (t.TryGetComponent(out MotionAgent agent))
            {
                agent.SnapAll();
            }
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for collections to enable easy staggering of Spring animations.
    /// </summary>
    public static class SpringListExtensions
    {
        /// <summary>
        /// Runs an asynchronous animation on every item in the list, with a time delay (stagger) between each start.
        /// <para>
        /// <b>Stackable:</b> Calling this again starts another "wave" on the same list without cancelling
        /// the ones already running, for a ripple effect.
        /// </para>
        /// <para>
        /// <b>Safe:</b> The cascade is tied to <paramref name="owner"/>'s lifetime — if it is destroyed or the
        /// game quits, the sequence stops immediately without errors.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The type of item in the list (e.g., Transform, SpriteRenderer).</typeparam>
        /// <param name="list">The list of items to animate.</param>
        /// <param name="owner">The script running this animation (usually <c>this</c>); its destruction
        /// cancels the cascade, so no task outlives the object.</param>
        /// <param name="staggerGap">The delay in seconds between starting the animation for item N and item N+1.</param>
        /// <param name="action">
        /// The async function to run on each item.
        /// <br/>Arguments: <c>(item, index)</c>
        /// </param>
        /// <param name="unscaled">Measure the gap on unscaled time, so the cascade keeps running while the
        /// game is paused. Match this to the springs' <c>SpringConfig.Unscaled</c>.</param>
        /// <returns>A Task that completes once <b>every</b> item has finished its animation.</returns>
        public static Task Stagger<T>(
            this IList<T> list,
            MonoBehaviour owner,
            float staggerGap,
            System.Func<T, int, Task> action,
            bool unscaled = false
        )
        {
            return SpringStagger.Stagger(list, owner, staggerGap, action, unscaled);
        }
    }
}
