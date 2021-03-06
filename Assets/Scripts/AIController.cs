﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(fileName = "New AI Controller", menuName = "Controllers/AI")]
public class AIController : EntityController {

    public float goalDistanceFudge;

    public float distanceWeighting;

    public float angleWeighting;

    public float wanderRange;

    public float mapSizeX;

    public float mapSizeZ;

    public float maxTurnSpeed;

    public float minTargetTime;

    public float maxTargetTime;

    public float accuracy;

    public float continuedAccuracy;

    public float minFireDelay;

    public float maxFireDelay;

    public float maxGuessTime;

    public float moveCloserChance;

    public float xrayChance;

    public float minXrayTime;

    public float maxXrayTime;

    public override void OnDied(Entity entity) {
        base.OnDied(entity);
        entity.navMeshAgent.ResetPath();
        entity.navMeshAgent.enabled = false;
    }

    public override void OnUpdate(Entity entity) {
        if (entity.Health > 0) {
            //Use a decision tree to determine current action
            HandleAI(entity);
            //Update velocity
            entity.velocity = entity.navMeshAgent.velocity;
        }
    }

    public override void OnRespawn(Entity entity) {
        base.OnRespawn(entity);
        entity.navMeshAgent.enabled = true;
    }

    void HandleAI(Entity entity) {
        //Handle dead targets
        if (entity.currentTarget != null && entity.currentTarget.Health <= 0) {
            entity.currentTarget = null;
        }
        //Handle xraying first
        if (entity.xrayTime > 0.0f) {
            entity.xrayTime -= Time.deltaTime;
            if(entity.xrayTime < 0.0f) {
                entity.xrayTime = 0.0f;
                entity.isXRaying = false;
            }
            return;
        }
        if(entity.equippedGun.Clip == 0 && !entity.findingAmmo) {
            FindAmmo(entity);
            return;
        }
        //AI decision tree
        if (entity.currentTarget == null) { //No Target
            entity.SetStatus("AI: Searching for new target");
            FindTarget(entity);
            if (entity.currentTarget == null) { //No new targets
                Wander(entity);
            } else { //Tracking new target
                entity.SetStatus("AI: Target found");
                //TODO: Move to target
                entity.idleTimer = Random.Range(minTargetTime, maxTargetTime);
                entity.fireDelay = Random.Range(minFireDelay, maxFireDelay);
            }
        } else { //Continue tracking
            entity.SetStatus("AI: Tracking Target");
            AimAtTarget(entity);
            Wander(entity);
        }
    }

    private void FindAmmo(Entity entity) {
        float d = Mathf.Infinity;
        Vector3 closest = entity.transform.position;
        foreach(GameObject ammo in entity.gameController.ammo) {
            float sqr = (ammo.transform.position - entity.transform.position).sqrMagnitude;
            if(sqr < d) {
                d = sqr;
                closest = ammo.transform.position;
            }
        }
        entity.navMeshAgent.SetDestination(closest);
        entity.currentGoal = closest;
        entity.findingAmmo = true;
        entity.wandering = true;
    }

    void FindTarget(Entity entity) {
        //Loop through and find all visible entities
        List<Entity> possibleTargets = new List<Entity>();
        foreach (Entity ent in entity.gameController.entities) {
            if (ent != entity && ent.Health > 0 && EntityVisible(entity, ent)) {
                possibleTargets.Add(ent);
            }
        }
        //Don't target anything if noone is visible
        if (possibleTargets.Count == 0) {
            return;
        }
        //Get "closest" target
        float lowest = Mathf.Infinity;
        Entity closest = possibleTargets[0];
        foreach (Entity ent in possibleTargets) {
            Vector3 dif = (ent.transform.position - entity.transform.position);
            float dist = dif.magnitude;
            float w = dist * -distanceWeighting;
            float cosAng = Vector3.Dot(entity.transform.forward, dif) / dist;
            w += angleWeighting * (1 - cosAng);
            if (w < lowest) {
                lowest = w;
                closest = ent;
            }
        }
        entity.currentTarget = closest;
        entity.wandering = false;
    }

    bool EntityVisible(Entity from, Entity to) {
        //XRaying players are visible to all and players xraying can obviously see everyone
        if(from.isXRaying || to.isXRaying) {
            return true;
        }
        //Gonna have to settle with raycasts from eye to eye, even though this won't be quite true
        Vector3 offset = to.gameObject.transform.position - from.gameObject.transform.position;
        return !Physics.Raycast(from.gameObject.transform.position, offset, offset.magnitude, (1 << LayerMask.NameToLayer("LevelGeometry")));
    }

    void Wander(Entity entity) {
        //If reached current goal position, go somewhere else
        if (!entity.wandering || (entity.wandering && (entity.transform.position - entity.currentGoal).sqrMagnitude < goalDistanceFudge)) {
            if (entity.wandering) {
                entity.findingAmmo = false;
            }
            if (Random.Range(0, 100) < xrayChance) {
                entity.SetStatus("AI: XRaying");
                XRay(entity);
            } else {
                entity.SetStatus("AI: Wandering");
            }
            CreateWanderGoal(entity);
            entity.wandering = true;
        }
    }

    void CreateWanderGoal(Entity entity) {
        Vector2 randMove = Random.insideUnitCircle * wanderRange;

        if (randMove.x < -mapSizeX / 2) {
            randMove.x += mapSizeX;
        } else if (randMove.x > mapSizeX / 2) {
            randMove.x += -mapSizeX;
        }
        if (randMove.y < -mapSizeZ / 2) {
            randMove.y += mapSizeZ;
        }
        if (randMove.y < -mapSizeZ / 2) {
            randMove.y += mapSizeZ;
        }

        Vector3 target = new Vector3(randMove.x, 0.0f, randMove.y);

        Vector3 centre;

        if(entity.currentTarget != null && (entity.isXRaying || Random.Range(0, 100) < moveCloserChance)) {
            centre = entity.currentTarget.transform.position;
        } else {
            centre = entity.transform.position;
        }

        NavMeshHit hit;
        NavMesh.SamplePosition(target + centre, out hit, wanderRange, -1);

        entity.currentGoal = hit.position;
        entity.navMeshAgent.SetDestination(hit.position);
    }

    void AimAtTarget(Entity entity) {
        //Release trigger for non automatic
        if (entity.equippedGun.triggerHeld && !entity.equippedGun.gunProperties.auto) {
            entity.equippedGun.triggerHeld = false;
            entity.idleTimer = entity.equippedGun.gunProperties.fireRate;
        }
        //Release trigger for empty guns
        if (entity.equippedGun.Ammo == 0) {
            entity.idleTimer = entity.equippedGun.gunProperties.reloadTime + Random.Range(minTargetTime, maxTargetTime);
        }

        float dist = 0;

        //Check target is visible
        if(EntityVisible(entity, entity.currentTarget)) {
            dist = LookAtTarget(entity);
            entity.guessingTime = 0.0f;
            entity.lastSeen = entity.currentTarget.transform.position;
            entity.lastMoving = entity.currentTarget.velocity;
        } else {
            entity.guessingTime += Time.deltaTime;
            dist = GuessTarget(entity);
        }
       
        //Shoot periodically
        entity.idleTimer -= Time.deltaTime;
        float threshold = entity.hasShot ? continuedAccuracy : accuracy;
        if (entity.idleTimer <= 0.0f) {
            if (dist < threshold * threshold) {
                ShootAtTarget(entity);
            } else {
                entity.fireDelay = Random.Range(minFireDelay, maxFireDelay);
                entity.hasShot = false;
            }
        }
    }

    float LookAtTarget(Entity entity, Vector3 target) {
        //Rotate camera
        Vector3 diff = target - entity.transform.position;

        diff = diff.normalized;

        Vector3 lookDir = Quaternion.Euler(entity.lookAngle) * new Vector3(0.0f, 0.0f, 1.0f);

        Vector3 change = Vector3.RotateTowards(lookDir, diff, Mathf.Deg2Rad * maxTurnSpeed * Time.deltaTime, 0.0f);

        float horDist = change.z * change.z + change.x * change.x;
        horDist = Mathf.Sqrt(horDist);

        float dX = -Mathf.Atan2(change.y, horDist) * Mathf.Rad2Deg;
        float dY = Mathf.Atan2(change.x, change.z) * Mathf.Rad2Deg;

        float dist = (diff - change).magnitude;

        entity.lookAngle = new Vector3(dX, dY, 0.0f);
        UpdateRotation(entity);

        return dist;
    }

    float LookAtTarget(Entity entity) {
        return LookAtTarget(entity, entity.currentTarget.transform.position);
    }

    float GuessTarget(Entity entity) {
        Vector3 guess = entity.lastSeen + entity.lastMoving * entity.guessingTime;
        if(entity.guessingTime > maxGuessTime) {
            entity.currentTarget = null;
            entity.SetStatus("AI: Lost sight of target");
        }
        return LookAtTarget(entity, guess);
    }

    void ShootAtTarget(Entity entity) {
        entity.fireDelay -= Time.deltaTime;
        //First shot must have a delay to aim
        if (entity.fireDelay <= 0) {
            entity.equippedGun.triggerHeld = true;
            entity.hasShot = true;
        }
    }

    void XRay(Entity entity) {
        entity.isXRaying = true;
        entity.xrayTime = Random.Range(minXrayTime, maxXrayTime);
    }
}