using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class CameraEventManager : MonoBehaviour
{
	public static CameraEventManager Instance { get; private set; }
	
	[SerializeField] private CinemachineCamera cam;
	private CinemachinePositionComposer composer;
	[SerializeField] private GameObject cameraTarget;

	public float shakeDuration;
	public float shakeFactor;

	private float originalSize;
	
	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}
		
		cam = GetComponent<CinemachineCamera>();
		composer = GetComponent<CinemachinePositionComposer>();
		originalSize = composer.CameraDistance;
	}

	private void Update()
	{

	}
	
	//private void MoveCameraToPoint(Vector3 point, float time = 1) => cam.transform.position = Vector3.Lerp(cam.transform.position, point, time);
	public void ZoomCamera(float zoom, float time = 1) => composer.CameraDistance = Mathf.Lerp(composer.CameraDistance, zoom, time);
	public void ChangeTarget(GameObject target)
	{
		cam.Follow = target.transform;
		cameraTarget = target;
	}

	public IEnumerator ForceTarget(GameObject target, float period = 1)
	{
		var p = period;
		while (p > 0 || cameraTarget != target)
		{
			p -= Time.deltaTime;
			cameraTarget = GameObject.FindGameObjectWithTag(target.tag) ?? GameObject.Find(target.name) ?? target;
			yield return null;
		}
		if (cameraTarget != target)
		{
			Debug.LogError($"[CameraEventManager] HUHH??? WHERE IS EVERYTHING?!?! ({target?.name} {target?.tag})");
			ResetCamera();
		}
		else
		{
			cam.Follow = target.transform;
			cameraTarget = target;
		}
	}

	public void ShakeCamera()
	{
		// entah sham codin erre
		// For we yet lack the knowledge of fulfilling the duty of this method.
	}

	private void ResetCamera()
	{
		composer.CameraDistance = originalSize;
		cam.Follow = null;
		enabled = false;
	}
}
