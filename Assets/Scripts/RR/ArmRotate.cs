﻿using UnityEngine;
using UnityEngine.UI;

public class ArmRotate : MonoBehaviour
{
    public Communication communication;
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagSwitchReference = "SwitchReferenceRotate";
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagSwitchStep = "SwitchStepRotate";
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagDirection = "MotorRotateDirection";
    [Tooltip("A name of tag (defined in config-RR.json)")]
    public string tagMovement = "MotorRotateMovement";
    [Tooltip("Key of steps limit (defined in config-RR.json)")]
    public string strStepsLimit = "RotateStepsLimit";

    public Transform arm;
    public Transform switchReference;
    public Transform objWarningLimitSwitch;
    public Transform objWarningLimitOpenEnd;
    public Transform objPosition; // object that defines position and is used to trigger pulses
    public Transform objRotationRef;
    public Transform objPositionEnd;
    public GameObject warningSign;
    
    float PLCCycle; // target cycle of the PLC in seconds
    readonly float speed_factor = 1.0f; // Horizontal speed between 1 (i.e. max speed) and 0.1 (min. speed)
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
    Vector3 currentPositionVector;
    Vector3 endPositionVector;

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

    // Initialization
    void Awake()
    {
        PLCCycle = float.Parse(communication.appConfig.TrainingModelSpecific["PLCCycle"]);
        stepsLimit = int.Parse(communication.appConfig.TrainingModelSpecific[strStepsLimit]); // number of pulses on the distance between arm and the limit

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

        // Angle between the limit and starting position of the table (the smaller angle - ie 90 degrees)
        currentPositionVector = objPosition.position - objRotationRef.position;
        endPositionVector = objPositionEnd.position - objRotationRef.position;

        safeLimit = Vector3.Angle(currentPositionVector, endPositionVector) + 180;
        pulseCellUnit = safeLimit / (stepsLimit + 1); // length of one pulse cell
        currentMoveProgress = safeLimit; // we are at full distance from limit
        pulseCellCurrent = stepsLimit + 1;
        pulseCellOld = pulseCellCurrent;

        // Vector for rotation in y axis
        movementVector = new Vector3(0, speed_factor * pulseCellUnit / framesPerUnitAngle, 0);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.transform == switchReference)
        {
            referenceSwitchActive = true;
        }
        if (other.transform == objWarningLimitSwitch)
        {
            warningSwitchActive = true;
        }
        if (other.transform == objWarningLimitOpenEnd)
        {
            warningOpenEndActive = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.transform == switchReference)
        {
            referenceSwitchActive = false;
        }
        if (other.transform == objWarningLimitSwitch)
        {
            warningSwitchActive = false;
        }
        if (other.transform == objWarningLimitOpenEnd)
        {
            warningOpenEndActive = false;
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
        currentPositionVector = objPosition.position - objRotationRef.position;
        currentMoveProgress = Vector3.Angle(currentPositionVector, endPositionVector);

        if (currentPositionVector.x > 0)
            currentMoveProgress = 360 - currentMoveProgress;
        // Legal angles lie between 0 and 270. If table turns slightly too much, there is jump 
        // to ~360 degrees. Consider some safe margins
        if (currentMoveProgress > 300)
            currentMoveProgress = 0;
        if (currentMoveProgress > 270 && currentMoveProgress < 280)
            currentMoveProgress = 270;

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
        if (!warningOpenEndActive)
        {
            arm.Rotate(movementVector);
        }
    }

    void MoveTowardSwitch()
    {
        if (!warningSwitchActive)
        {
            arm.Rotate(-movementVector);
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
