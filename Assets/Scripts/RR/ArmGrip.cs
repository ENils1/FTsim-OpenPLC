﻿using UnityEngine;
using UnityEngine.UI;

public class ArmGrip : MonoBehaviour
{
    public Communication communication;
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagSwitchReference = "SwitchReferenceGrip";
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagSwitchStep = "SwitchStepGrip";
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagDirection = "MotorGripDirection";
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagMovement = "MotorGripMovement";
    [Tooltip("Key of steps limit (defined in config-RR.json)")]
    public string strStepsLimit = "GripStepsLimit";

    public Transform switchReference;
    public Transform objWarningLimitSwitch;
    public Transform gripperLeft;
    public Transform gripperRight;
    public Transform gripperCenter;
    public Transform objPosition; // object that defines position and is used to trigger pulses
    public Transform objGripped = null;
    public GameObject warningSign;
    public Transform gripperRightTrigger;

    bool isGripped;
    Collider workpieceCollider;

    float PLCCycle; // target cycle of the PLC in seconds
    readonly float speedFactor = 1.0f; // Horizontal speed between 1 (i.e. max speed) and 0.1 (min. speed)
    readonly int framesPerUnitAngle = 2; // each frame, move by unit/framesPerUnitDist

    bool referenceSwitchActive;
    bool warningSwitchActive;
    bool warningOpenEndActive;

    int switchReferenceValue;
    int switchReferenceNewValue;
    bool switchReferenceForceTrue;
    bool switchReferenceForceFalse;
    int switchStepValue;
    int switchStepNewValue;
    bool switchStepForceTrue;
    bool switchStepForceFalse;

    int stepsLimit;
    Vector3 movementVector;

    float dt;
    float safeLimit;
    float pulseCellUnit;
    float currentMoveProgress;
    int pulseCellCurrent;
    int pulseCellOld;
    bool pulseState = false;
    bool pulseStateOld = false;
    float timeHigh = 0.0f;
    float timeLow = 0.0f;
    bool allowedToMove = true;

    public void UpdateGripState(bool state) {
        isGripped = state;
    }
    
    // Initialization
    void Awake()
    {
        PLCCycle = float.Parse(communication.appConfig.TrainingModelSpecific["PLCCycle"]);
        stepsLimit = int.Parse(communication.appConfig.TrainingModelSpecific[strStepsLimit]); // number of pulses on the distance between arm and the limit

        isGripped = false;
        referenceSwitchActive = false;
        warningSwitchActive = false;
        warningOpenEndActive = false;
        warningSign.SetActive(false);

        switchReferenceValue = -1;
        switchReferenceForceTrue = false;
        switchReferenceForceFalse = false;

        switchStepValue = -1;
        switchStepForceTrue = false;
        switchStepForceFalse = false;

        // Distance between the limit and starting position of the arm
        safeLimit = Vector3.Distance(objPosition.position, gripperCenter.position);
        pulseCellUnit = safeLimit / (stepsLimit + 3); // length of one pulse cell; add 3 to finetune target value
        currentMoveProgress = safeLimit; // we are at full distance from limit
        pulseCellCurrent = stepsLimit + 3;
        pulseCellOld = pulseCellCurrent;

        // Vector for moving in y axis
        movementVector = new Vector3
        {
            x = speedFactor * pulseCellUnit / framesPerUnitAngle
        };
    }

    

    void OnTriggerEnter(Collider otherCollider)
    {
        if (otherCollider.transform == switchReference)
        {
            referenceSwitchActive = true;
        }
        else if (otherCollider.transform == objWarningLimitSwitch)
        {
            warningSwitchActive = true;
        }
        else if (otherCollider.transform == gripperRightTrigger)
        {
            warningOpenEndActive = true;
        }
    }

    void OnTriggerExit(Collider otherCollider)
    {
        if (otherCollider.transform == switchReference)
        {
            referenceSwitchActive = false;
        }
        if (otherCollider.transform == objWarningLimitSwitch)
        {
            warningSwitchActive = false;
        }
        else if (otherCollider.transform == gripperRightTrigger)
        {
            warningOpenEndActive = false;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        
        if (collision.gameObject.CompareTag("Workpiece"))
        {
            workpieceCollider = collision.collider;
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Workpiece"))
        {
            workpieceCollider = null;
        }

    }

    // Update is called once per frame
    void Update()
    {
        UpdateWarningSignVisibility();

        // Compute state of a step switch (movement impulse)
        // Duration of a previous frame
        dt = Time.deltaTime;
        // This method updates the counters of low and high timers,
        // alters timeHigh or timeLow.
        UpdateTimeCounters(dt);

        // Check for pulse triggering
        currentMoveProgress = Vector3.Distance(objPosition.position, gripperCenter.position);

        // Check if pulse cell needs to be changed (high <-> low).
        // This method alters allowedToMove and pulseState.
        DetectPulseCellChange();

        // Consider forced values for reference switch and impulse
        switchReferenceNewValue = referenceSwitchActive ? 1 : 0;
        WriteOnChange(tagSwitchReference, switchReferenceValue, switchReferenceNewValue, switchReferenceForceTrue, switchReferenceForceFalse);
        switchReferenceValue = switchReferenceNewValue;

        switchStepNewValue = pulseState ? 1 : 0;
        WriteOnChange(tagSwitchStep, switchStepValue, switchStepNewValue, switchStepForceTrue, switchStepForceFalse);
        switchStepValue = switchStepNewValue;
    }
    void FixedUpdate()
    {
        // Movement control
        if (allowedToMove)
        {
            if (communication.ReadCoil(tagMovement))
            {
                if (communication.ReadCoil(tagDirection))
                {
                    MoveTowardSwitch();
                }
                else
                {
                    MoveTowardOpenEnd();
                }
            }
        }
    }
    void MoveTowardOpenEnd()
    {
        if (!warningOpenEndActive && !isGripped)
        {
            gripperRight.Translate(movementVector);
            gripperLeft.Translate(movementVector);
        }
    }

    void MoveTowardSwitch()
    {
        if (!warningSwitchActive)
        {
            gripperRight.Translate(-movementVector);
            gripperLeft.Translate(-movementVector);
            objGripped = null;
        }
    }
    void UpdateWarningSignVisibility()
    {
        // Show/hide warning sign if off limits
        if (warningOpenEndActive || warningSwitchActive)
        {
            warningSign.SetActive(true);
        }
        else
        {
            warningSign.SetActive(false);
        }
    }

    void DetectPulseCellChange()
    {
        // Detect cell change - the reference object has entered different cell
        pulseCellCurrent = Mathf.FloorToInt(currentMoveProgress / pulseCellUnit);

        if (pulseCellCurrent != pulseCellOld)
        {
            // Check if pulse was made right for previous cell: pulse has to be low long enough
            if (!pulseState && timeLow >= PLCCycle)
            {
                // Allow movement
                allowedToMove = true;
                // Update current pulse cell
                pulseCellOld = pulseCellCurrent;

                // Trigger new pulse
                pulseState = true;
            }
            else
            {
                allowedToMove = false;

                if (pulseState && timeHigh >= PLCCycle)
                {
                    // We have to wait for low still
                    pulseState = false;
                }
            }
        }
        else
        {
            // Cell not changed - ensure pulse width
            if (pulseState)
            {
                // pulse high
                if (timeHigh >= PLCCycle)
                {
                    // set pulse to low
                    pulseState = false;
                }
            }
        }
    }

    void UpdateTimeCounters(float dt)
    {
        // Update counters for low and high part of clock period
        if (pulseState)
        {
            // positive edge - reset 
            if (!pulseStateOld)
            {
                timeHigh = 0.0f;
                pulseStateOld = true;
            }
            // pulse is high
            timeHigh += dt;
        }
        else
        {
            // negative edge - reset 
            if (pulseStateOld)
            {
                timeLow = 0.0f;
                pulseStateOld = false;
            }
            // pulse is low
            timeLow += dt;
        }
    }

    void WriteOnChange(string tag, int sensorValue, int newValue, bool forceTrue, bool forceFalse)
    {
        if (sensorValue != newValue)
        {
            //  If both forces are inactive, write to PLC
            if (!(forceFalse || forceTrue))
            {
                communication.WriteDiscreteInput(tag, newValue);
            }
        }
    }
    public void SwitchReferenceForceTrueOnChange(Toggle change)
    {
        switchReferenceForceTrue = change.isOn;
        // Write true to PLC if isOn = true
        // Write value to PLC if isOn = false
        int val = 1;
        if (!change.isOn)
        {
            val = switchReferenceValue;
        }
        communication.WriteDiscreteInput(tagSwitchReference, val);
    }

    public void SwitchReferenceForceFalseOnChange(Toggle change)
    {
        switchReferenceForceFalse = change.isOn;
        // Write false to PLC if isOn = true
        // Write value to PLC if isOn = false
        int val = 0;
        if (!change.isOn)
        {
            val = switchReferenceValue;
        }
        communication.WriteDiscreteInput(tagSwitchReference, val);
    }

    public void SwitchStepForceTrueOnChange(Toggle change)
    {
        switchStepForceTrue = change.isOn;
        // Write true to PLC if isOn = true
        // Write value to PLC if isOn = false
        int val = 1;
        if (!change.isOn)
        {
            val = switchStepValue;
        }
        communication.WriteDiscreteInput(tagSwitchStep, val);
    }

    public void SwitchStepForceFalseOnChange(Toggle change)
    {
        switchStepForceFalse = change.isOn;
        // Write false to PLC if isOn = true
        // Write value to PLC if isOn = false
        int val = 0;
        if (!change.isOn)
        {
            val = switchStepValue;
        }
        communication.WriteDiscreteInput(tagSwitchStep, val);
    }

}
