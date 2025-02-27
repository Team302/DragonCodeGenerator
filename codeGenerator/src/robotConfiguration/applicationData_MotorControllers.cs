using ApplicationData;
using Configuration;
using DataConfiguration;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using static ApplicationData.motorControlData;
using static ApplicationData.MotorController;

//todo handle optional elements such as followID in a motorcontroller
//todo the range of pdpID for ctre is 0-15, for REV it is 0-19. How to adjust the range allowed in the GUI. If initially REV is used and an id > 15 is used, then user chooses CTRE, what to do?
//todo make mechanism instances separate files so that it is easier for multiple people to work on the robot in parallel
//todo run a sanity check on a click of a button or on every change?
//todo in the treeview, place the "name" nodes at the top
//todo in the robot code, check that an enum belonging to another robot is not used
//todo check naming convention
//todo getDisplayName gets called multiple times when a solenoid name is changed in a mechanism
//todo handle DistanceAngleCalcStruc should this be split into 2 separate structs? one ofr dist , 2nd for angle?
//todo when mechanisms are renamed, the GUIDs get messed up
//todo if a decorator mod file exists, do not write it
//todo show the DataDescription information
//todo target physical units should not be editable in the mechanism instance
//todo add DataDescription for the robot elements
//todo zoom so that the text is larger
//todo handle chassis like a special mechanism

// =================================== Rules =====================================
// A property named __units__ will be converted to the list of physical units
// A property named value__ will not be shown in the tree directly. Its value is shown in the parent node
// Attributes are only allowed on the standard types (uint, int, double, bool) and on doubleParameter, unitParameter, intParameter, boolParameter
// The attribute PhysicalUnitsFamily can only be applied on doubleParameter, uintParameter, intParameter, boolParameter
// A class can only contain one List of a particular type

namespace ApplicationData
{
    [Serializable()]
    [XmlInclude(typeof(TalonFX))]
    [XmlInclude(typeof(TalonFXS))]
    [XmlInclude(typeof(TalonSRX))]
    [XmlInclude(typeof(SparkMax))]
    [XmlInclude(typeof(SparkMaxMonitored))]
    [XmlInclude(typeof(SparkFlex))]
    [XmlInclude(typeof(SparkFlexMonitored))]
    public class MotorController : baseRobotElementClass
    {
        public enum Enabled { Yes, No }
        public enum InvertedValue { CounterClockwise_Positive, Clockwise_Positive }
        public enum NeutralModeValue { Coast, Brake }

        public enum SwitchConfiguration
        {
            NormallyOpen,
            NormallyClosed
        };

        public enum RemoteSensorSource
        {
            Off,
            TalonSRX_SelectedSensor,
            Pigeon_Yaw,
            Pigeon_Pitch,
            Pigeon_Roll,
            CANifier_Quadrature,
            CANifier_PWMInput0,
            CANifier_PWMInput1,
            CANifier_PWMInput2,
            CANifier_PWMInput3,
            GadgeteerPigeon_Yaw,
            GadgeteerPigeon_Pitch,
            GadgeteerPigeon_Roll,
            CANCoder,
            TalonFX_SelectedSensor = TalonSRX_SelectedSensor,
        };

        public enum MOTOR_TYPE
        {
            UNKNOWN_MOTOR = -1,
            FALCON500,
            NEOMOTOR,
            NEO500MOTOR,
            VORTEX,
            CIMMOTOR,
            MINICIMMOTOR,
            BAGMOTOR,
            PRO775,
            ANDYMARK9015,
            ANDYMARKNEVEREST,
            ANDYMARKRS775125,
            ANDYMARKREDLINEA,
            REVROBOTICSHDHEXMOTOR,
            BANEBOTSRS77518V,
            BANEBOTSRS550,
            MODERNROBOTICS12VDCMOTOR,
            JOHNSONELECTRICALGEARMOTOR,
            TETRIXMAXTORQUENADOMOTOR,
            NONE,
            MAX_MOTOR_TYPES
        };

        public Enabled ControllerEnabled { get; set; }

        [Serializable]
        public class DistanceAngleCalcStruc : baseDataClass
        {
            [DefaultValue(1.0)]
            public doubleParameter gearRatio { get; set; }

            [DefaultValue(1.0)]
            [PhysicalUnitsFamily(physicalUnit.Family.length)]
            public doubleParameter diameter { get; set; }
            public boolParameter isDistance { get; set; }
            [DefaultValue(false)]

            public DistanceAngleCalcStruc()
            {
                defaultDisplayName = this.GetType().Name;
            }

            public string getDefinition(string namePrePend)
            {
                string fullName = getName(namePrePend);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format("{0} {1};", this.GetType().Name, fullName));

                foreach (PropertyInfo pi in GetType().GetProperties())
                {
                    Object obj = pi.GetValue(this);
                    // PhysicalUnitsFamilyAttribute unitsAttr = this.GetType().GetCustomAttribute<PhysicalUnitsFamilyAttribute>();

                    string rightValue = obj.ToString();
                    if (pi.Name == "diameter")
                    {
                        string units = generatorContext.theGeneratorConfig.getWPIphysicalUnitType(diameter.physicalUnits);
                        rightValue = string.Format("units::length::inch_t({0}({1})).to<double>()", units, rightValue);
                    }

                    sb.AppendLine(string.Format("{0}.{1} = {2} ;", fullName, pi.Name, rightValue));
                }

                return sb.ToString();
            }

            public string getName(string namePrePend)
            {
                return namePrePend + "CalcStruct";
            }
        }
        public DistanceAngleCalcStruc theDistanceAngleCalcInfo { get; set; }

        [Serializable]
        public class VoltageRamping : baseDataClass
        {
            [DefaultValue(0)]
            [PhysicalUnitsFamily(physicalUnit.Family.time)]
            public doubleParameter openLoopRampTime { get; set; }

            [DefaultValue(0)]
            [PhysicalUnitsFamily(physicalUnit.Family.time)]
            public doubleParameter closedLoopRampTime { get; set; }

            [DefaultValue(false)]
            public boolParameter enableClosedLoop { get; set; }

            public VoltageRamping()
            {
                defaultDisplayName = this.GetType().Name;
            }
        }
        public VoltageRamping voltageRamping { get; set; }


        [XmlIgnore]
        [Constant()]
        public string motorControllerType { get; protected set; }

        [DefaultValue(MOTOR_TYPE.UNKNOWN_MOTOR)]
        [ConstantInMechInstance]
        public MOTOR_TYPE motorType { get; set; }

        [DefaultValue(0u)]
        [Range(typeof(uint), "0", "62")]
        [DataDescription("The ID that is used to form the device CAD ID")]
        [DataDescription("ID 0 is normally reserved for the roborio")]
        public uintParameter canID { get; set; }

        [DefaultValue(CAN_BUS.rio)]
        [ConstantInMechInstance]
        public CAN_BUS canBusName { get; set; }

        [DefaultValue(0u)]
        [Range(typeof(uint), "0", "19")]// REV is 0-19, CTRE 0-15, cannot handle 2 ranges for now
        public uintParameter pdpID { get; set; }

        [DefaultValue(0u)]
        [Range(typeof(uint), "0", "62")]
        [ConstantInMechInstance]
        public uintParameter followID { get; set; }

        [DefaultValue(false)]
        [ConstantInMechInstance]
        public boolParameter enableFollowID { get; set; }



        [Serializable]
        public class RemoteSensor : baseDataClass
        {
            [DefaultValue(RemoteSensorSource.Off)]
            [ConstantInMechInstance]
            public RemoteSensorSource Source { get; set; }

            [DefaultValue(0u)]
            [Range(typeof(uint), "0", "62")]
            [DataDescription("The ID that is used to form the device CAD ID")]
            public uintParameter CanID { get; set; }

            public RemoteSensor()
            {
                defaultDisplayName = this.GetType().Name;
            }

            public doubleParameter sensorToMechanismRatio { get; set; }
        }
        public RemoteSensor remoteSensor { get; set; }

        public enum FusedSyncChoice
        {
            FUSED,
            SYNC
        }

        [Serializable]
        public class FusedSyncCANcoder : baseDataClass
        {
            [DefaultValue(false)]
            [ConstantInMechInstance]
            public boolParameter enable { get; set; }

            [ConstantInMechInstance]
            public CANcoderInstance fusedCANcoder { get; set; }

            [ConstantInMechInstance]
            public doubleParameter sensorToMechanismRatio { get; set; }

            [ConstantInMechInstance]
            public doubleParameter rotorToSensorRatio { get; set; }

            [ConstantInMechInstance]
            public FusedSyncChoice fusedSyncChoice { get; set; }

            public FusedSyncCANcoder()
            {
                defaultDisplayName = "FusedSyncCANcoder";
            }
        }

        public FusedSyncCANcoder fusedSyncCANcoder { get; set; }

        [DefaultValue(false)]
        [ConstantInMechInstance]
        public boolParameter sensorIsInverted { get; set; }

        public MotorController()
        {
            motorControllerType = this.GetType().Name;
        }
        public override string AsMemberVariableName()
        {
            string motorTypeAdder = generatorContext.theMechanism.MotorControllers.Count(m => m.name == name) > 1 ? GetType().Name : "";
            return string.Format("{0}", AsMemberVariableName(name + motorTypeAdder));
        }

        public override string ToUpperCamelCase()
        {
            string motorTypeAdder = generatorContext.theMechanism.MotorControllers.Count(m => m.name == name) > 1 ? GetType().Name : "";
            return string.Format("{0}", ToUpperCamelCase(name + motorTypeAdder));
        }

        public override List<string> generateInitialization()
        {
            List<string> initCode = new List<string>();

            initCode.Add(string.Format(@"{0}->SetVoltageRamping( units::time::second_t({1}({2})).to<double>(),
                                                                 units::time::second_t({3}({4})).to<double>() );",
                                                        AsMemberVariableName(),
                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(voltageRamping.openLoopRampTime.physicalUnits),
                                                        voltageRamping.openLoopRampTime.value,
                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(voltageRamping.closedLoopRampTime.physicalUnits),
                                                        voltageRamping.enableClosedLoop.value ? voltageRamping.closedLoopRampTime.value : 0.0));

            initCode.Add(string.Format("{0}->SetSensorInverted( {1});",
                                                        AsMemberVariableName(),
                                                        sensorIsInverted.ToString().ToLower()));

            return initCode;
        }

        override public List<string> generateObjectAddToMaps()
        {
            string creation = string.Format(@"m_motorMap[{0}{1}->GetType()] = new BaseMechMotor(m_ntName, 
                                                                                            {0}{1}, 
                                                                                            BaseMechMotor::EndOfTravelSensorOption::NONE, 
                                                                                            nullptr, 
                                                                                            BaseMechMotor::EndOfTravelSensorOption::NONE, 
                                                                                            nullptr)",
                                                                                name,
                                                                                getImplementationName());

            return new List<string> { creation };
        }

        override public List<string> generateDefinition()
        {
            return new List<string> { string.Format("{0}* {1};", getImplementationName(), AsMemberVariableName()) };
        }

        override public List<string> generateDefinitionGetter()
        {
            return new List<string> { string.Format("{0}* Get{1}() const {{return {2};}}", getImplementationName(), ToUpperCamelCase(), AsMemberVariableName()) };
        }

        virtual public string GenerateTargetMemberVariable(motorControlData mcd)
        {
            return "";
        }
        virtual public string GenerateGenericTargetMemberVariable()
        {
            return "";
        }
        virtual public List<string> GenerateTargetUpdateFunctions(motorControlData mcd)
        {
            return new List<string>();
        }
        virtual public string GenerateTargetUpdateFunctionCall(motorControlData mcd, double value)
        {
            return "";
        }
        virtual public string GenerateCyclicGenericTargetRefresh()
        {
            return "";
        }
        virtual public string GeneratePIDSetFunction(motorControlData mcd, mechanismInstance mi)
        {
            return "";
        }
        virtual public string GeneratePIDSetFunctionDeclaration(motorControlData mcd, mechanismInstance mi)
        {
            return "";
        }
        virtual public string GeneratePIDSetFunctionCall(motorControlData mcd, mechanismInstance mi)
        {
            return "";
        }
        override public List<string> generateElementNames()
        {
            Type baseType = GetType();
            while ((baseType.BaseType != typeof(object)) && (baseType.BaseType != typeof(baseRobotElementClass)))
                baseType = baseType.BaseType;
            if (generatorContext.theMechanismInstance != null)
            {
                int count = generatorContext.theMechanismInstance.mechanism.MotorControllers.FindAll(n => n.name == name).Count;
                if (count > 1)
                    return new List<string> { string.Format("{3}::{0}_{1}_{2}", ToUnderscoreCase(generatorContext.theMechanismInstance.name), ToUnderscoreCase(name), ToUnderscoreCase(this.GetType().Name), ToUnderscoreCase(baseType.Name)) };
                return new List<string> { string.Format("{2}::{0}_{1}", ToUnderscoreCase(generatorContext.theMechanismInstance.name), ToUnderscoreCase(name), ToUnderscoreCase(baseType.Name)) };
            }
            else if (generatorContext.theMechanism != null)
            {
                return new List<string> { string.Format("{2}::{0}_{1}", ToUnderscoreCase(generatorContext.theMechanism.name), ToUnderscoreCase(name), ToUnderscoreCase(baseType.Name)) };
            }
            else if (generatorContext.theRobot != null)
                return new List<string> { string.Format("{1}::{0}", ToUnderscoreCase(name), ToUnderscoreCase(baseType.Name)) };
            else
                return new List<string> { "generateElementNames got to the else statement...should not be here" };
        }
    }

    [Serializable]
    public class CurrentLimits : baseDataClass
    {
        [DefaultValue(false)]
        public boolParameter enableStatorCurrentLimit { get; set; }

        [DefaultValue(0)]
        [Range(typeof(double), "0", "120.0")] //todo choose a valid range
        [PhysicalUnitsFamily(physicalUnit.Family.current)]
        public doubleParameter statorCurrentLimit { get; set; }

        [DefaultValue(false)]
        public boolParameter enableSupplyCurrentLimit { get; set; }

        [DefaultValue(0)]
        [Range(typeof(double), "0", "70.0")] //todo choose a valid range
        [PhysicalUnitsFamily(physicalUnit.Family.current)]
        public doubleParameter supplyCurrentLimit { get; set; }

        [DefaultValue(0)]
        [Range(typeof(double), "0", "40.0")] //todo choose a valid range
        [PhysicalUnitsFamily(physicalUnit.Family.current)]
        public doubleParameter supplyCurrentThreshold { get; set; }

        [DefaultValue(0)]
        [Range(typeof(double), "0", "40.0")] //todo choose a valid range
        [PhysicalUnitsFamily(physicalUnit.Family.time)]
        public doubleParameter supplyTimeThreshold { get; set; }

        public CurrentLimits()
        {
            defaultDisplayName = "CurrentLimits";
        }
    }

    [Serializable]
    public class ConfigHWLimitSW : baseDataClass
    {
        public enum ForwardLimitSourceValue { LimitSwitchPin, RemoteCANdiS1, RemoteCANdiS2 }
        public enum ForwardLimitTypeValue { NormallyOpen, NormallyClosed }
        public enum ReverseLimitSourceValue { LimitSwitchPin, RemoteCANdiS1, RemoteCANdiS2 }
        public enum ReverseLimitTypeValue { NormallyOpen, NormallyClosed }

        public boolParameter enableForward { get; set; }

        public intParameter remoteForwardSensorID { get; set; }

        public boolParameter forwardResetPosition { get; set; }

        [PhysicalUnitsFamily(physicalUnit.Family.angle)]
        public doubleParameter forwardPosition { get; set; }

        public ForwardLimitSourceValue forwardType { get; set; }

        public ForwardLimitTypeValue forwardOpenClose { get; set; }

        public boolParameter enableReverse { get; set; }

        public intParameter remoteReverseSensorID { get; set; }

        public boolParameter reverseResetPosition { get; set; }

        [PhysicalUnitsFamily(physicalUnit.Family.angle)]
        public doubleParameter reversePosition { get; set; }

        public ReverseLimitSourceValue revType { get; set; }

        public ReverseLimitTypeValue revOpenClose { get; set; }

        public ConfigHWLimitSW()
        {
            defaultDisplayName = "ConfigHWLimitSW";
        }
    }

    [Serializable]
    public class ConfigMotorSettings : baseDataClass
    {
        [DefaultValue(0)]
        [Range(typeof(double), "0", "100")]
        [PhysicalUnitsFamily(physicalUnit.Family.percent)]
        [ConstantInMechInstance]
        public doubleParameter deadbandPercent { get; set; }

        [DefaultValue(1)]
        [Range(typeof(double), "0", "1.0")]
        [PhysicalUnitsFamily(physicalUnit.Family.none)]
        [ConstantInMechInstance]
        public doubleParameter peakForwardDutyCycle { get; set; }

        [DefaultValue(-1)]
        [Range(typeof(double), "-1.0", "0.0")]
        [PhysicalUnitsFamily(physicalUnit.Family.none)]
        [ConstantInMechInstance]
        public doubleParameter peakReverseDutyCycle { get; set; }

        [DefaultValue(InvertedValue.CounterClockwise_Positive)]
        public InvertedValue inverted { get; set; }

        [DefaultValue(NeutralModeValue.Coast)]
        [ConstantInMechInstance]
        public NeutralModeValue mode { get; set; }

        public ConfigMotorSettings()
        {
            int index = this.GetType().Name.IndexOf("_");
            if (index > 0)
                defaultDisplayName = this.GetType().Name.Substring(0, index);
            else
                defaultDisplayName = this.GetType().Name;
        }
    }

    [Serializable]
    public class TalonBase : MotorController
    {
        public CurrentLimits theCurrentLimits { get; set; }

        public List<PIDFslot> PIDFs { get; set; }

        public ConfigHWLimitSW theConfigHWLimitSW { get; set; }

        public ConfigMotorSettings theConfigMotorSettings { get; set; }

        public TalonBase()
        {
        }

        override public List<string> generateInitialization()
        {
            List<string> initCode = new List<string>();

            if ((ControllerEnabled == Enabled.Yes))
            {
                string signatureWithoutReturn = string.Format("Initialize{0}{1}$$_ROBOT_FULL_NAME_$$()", this.GetType().Name, name, generatorContext.theMechanismInstance.name);

                initCode.Add(string.Format("CALL:{0}", signatureWithoutReturn));
                initCode.Add(string.Format("DECLARATION:void {0}", signatureWithoutReturn));
                initCode.Add("");
                initCode.Add(string.Format("void {0}::{1}", generatorContext.theMechanismInstance.name, signatureWithoutReturn));
                initCode.Add("{");


                initCode.Add(string.Format(@"   {10}Configuration configs{{}};
                                                configs.CurrentLimits.StatorCurrentLimit = {0}({1});
                                                configs.CurrentLimits.StatorCurrentLimitEnable = {2};
                                                configs.CurrentLimits.SupplyCurrentLimit = {3}({4});
                                                configs.CurrentLimits.SupplyCurrentLimitEnable = {5};
                                                configs.CurrentLimits.SupplyCurrentLowerLimit = {6}({7});
                                                configs.CurrentLimits.SupplyCurrentLowerTime = {8}({9});",

                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theCurrentLimits.statorCurrentLimit.__units__), theCurrentLimits.statorCurrentLimit.value,
                                                theCurrentLimits.enableStatorCurrentLimit.value.ToString().ToLower(),

                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theCurrentLimits.supplyCurrentLimit.__units__), theCurrentLimits.supplyCurrentLimit.value,
                                                theCurrentLimits.enableSupplyCurrentLimit.value.ToString().ToLower(),

                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theCurrentLimits.supplyCurrentThreshold.__units__), theCurrentLimits.supplyCurrentThreshold.value,
                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theCurrentLimits.supplyTimeThreshold.__units__), theCurrentLimits.supplyTimeThreshold.value,
                                                GetType().Name));

                initCode.Add("");

                initCode.Add(string.Format(@"   configs.Voltage.PeakForwardVoltage = units::voltage::volt_t(11.0);
                                            configs.Voltage.PeakReverseVoltage = units::voltage::volt_t(-11.0);"));

                if (voltageRamping.enableClosedLoop.value)
                    initCode.Add(string.Format(@"   configs.ClosedLoopRamps.TorqueClosedLoopRampPeriod = {0}({1});",
                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(voltageRamping.closedLoopRampTime.__units__), voltageRamping.closedLoopRampTime.value));
                else
                    initCode.Add(string.Format(@"   configs.OpenLoopRamps.VoltageOpenLoopRampPeriod = {0}({1});",
                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(voltageRamping.openLoopRampTime.__units__), voltageRamping.openLoopRampTime.value));
                
                initCode.Add(Environment.NewLine);

                initCode.Add(string.Format(@"	configs.HardwareLimitSwitch.ForwardLimitEnable = {0};
	                                            configs.HardwareLimitSwitch.ForwardLimitRemoteSensorID = {1};
	                                            configs.HardwareLimitSwitch.ForwardLimitAutosetPositionEnable = {2};
	                                            configs.HardwareLimitSwitch.ForwardLimitAutosetPositionValue = {16}({3});
	                                            configs.HardwareLimitSwitch.ForwardLimitSource = {4}::{5};
	                                            configs.HardwareLimitSwitch.ForwardLimitType = {6}::{7};

	                                            configs.HardwareLimitSwitch.ReverseLimitEnable = {8};
	                                            configs.HardwareLimitSwitch.ReverseLimitRemoteSensorID = {9};
	                                            configs.HardwareLimitSwitch.ReverseLimitAutosetPositionEnable = {10};
	                                            configs.HardwareLimitSwitch.ReverseLimitAutosetPositionValue = {17}({11});
	                                            configs.HardwareLimitSwitch.ReverseLimitSource = {12}::{13};
	                                            configs.HardwareLimitSwitch.ReverseLimitType = {14}::{15};",

                                                theConfigHWLimitSW.enableForward.value.ToString().ToLower(),
                                                theConfigHWLimitSW.remoteForwardSensorID.value,
                                                theConfigHWLimitSW.forwardResetPosition.value.ToString().ToLower(),
                                                theConfigHWLimitSW.forwardPosition.value,

                                                theConfigHWLimitSW.forwardType.GetType().Name,
                                                theConfigHWLimitSW.forwardType,
                                                theConfigHWLimitSW.forwardOpenClose.GetType().Name,
                                                theConfigHWLimitSW.forwardOpenClose,

                                                theConfigHWLimitSW.enableReverse.value.ToString().ToLower(),
                                                theConfigHWLimitSW.remoteReverseSensorID.value,
                                                theConfigHWLimitSW.reverseResetPosition.value.ToString().ToLower(),
                                                theConfigHWLimitSW.reversePosition.value,

                                                theConfigHWLimitSW.revType.GetType().Name,
                                                theConfigHWLimitSW.revType,
                                                theConfigHWLimitSW.revOpenClose.GetType().Name,
                                                theConfigHWLimitSW.revOpenClose,
                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theConfigHWLimitSW.forwardPosition.__units__),
                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theConfigHWLimitSW.reversePosition.__units__)));

                initCode.Add("");


                initCode.Add(string.Format(@"   configs.MotorOutput.Inverted = {0}::{1};
                                                configs.MotorOutput.NeutralMode = {2}::{3};
                                                configs.MotorOutput.PeakForwardDutyCycle = {4};
                                                configs.MotorOutput.PeakReverseDutyCycle = {5};
                                                configs.MotorOutput.DutyCycleNeutralDeadband = {6};",

                                                theConfigMotorSettings.inverted.GetType().Name, theConfigMotorSettings.inverted,
                                                theConfigMotorSettings.mode.GetType().Name, theConfigMotorSettings.mode,
                                                theConfigMotorSettings.peakForwardDutyCycle.value,
                                                theConfigMotorSettings.peakReverseDutyCycle.value,
                                                theConfigMotorSettings.deadbandPercent.value));

                string sensorSource = "FeedbackSensorSourceValue::RemoteCANcoder";
                if (fusedSyncCANcoder.enable.value == true)
                {
                    sensorSource = fusedSyncCANcoder.fusedSyncChoice == FusedSyncChoice.FUSED
                        ? "FeedbackSensorSourceValue::FusedCANcoder"
                        : "FeedbackSensorSourceValue::SyncCANcoder";
                }

                initCode.Add(Environment.NewLine);

                if (enableFollowID.value)
                {
                    initCode.Add(Environment.NewLine);

                    initCode.Add(string.Format(@"   ctre::phoenix::StatusCode status = ctre::phoenix::StatusCode::StatusCodeNotInitialized;                                     
                                                for(int i = 0; i < 5; ++i)
                                                {{
                                                    status = {0}->GetConfigurator().Apply(configs, units::time::second_t(0.25));
                                                    if (status.IsOK())
                                                        break;
                                                }}
                                                if (!status.IsOK())
                                                    Logger::GetLogger()->LogData(LOGGER_LEVEL::ERROR, ""{0}"",""{0} Status"", status.GetName());
                                                ",

                                                    AsMemberVariableName()));

                    initCode.Add(Environment.NewLine);

                    initCode.Add(string.Format(@"   {0}->SetControl(ctre::phoenix6::controls::StrictFollower{{{1}}});",
                                    AsMemberVariableName(), followID.value));
                }
                else
                {
                    if (GetType() != typeof(TalonFXS)) // figure out once TalonFXSes are available
                    {
                        if (fusedSyncCANcoder.enable.value)
                        {
                            CANcoder cc = generatorContext.theMechanismInstance.mechanism.cancoder.Find(c => c.name == this.fusedSyncCANcoder.fusedCANcoder.name);
                            if (cc != null)
                            {
                                initCode.Add(string.Format(@"   configs.Feedback.FeedbackRemoteSensorID = {1};
                                                    configs.Feedback.FeedbackSensorSource = {2};
                                                    configs.Feedback.SensorToMechanismRatio = {3};
                                                    configs.Feedback.RotorToSensorRatio = {4};",
                                                                AsMemberVariableName(),
                                                                cc.canID.value,
                                                                sensorSource,
                                                                fusedSyncCANcoder.sensorToMechanismRatio.value,
                                                                fusedSyncCANcoder.rotorToSensorRatio.value));
                            }
                            else
                            {
                                LogProgress($"Can Coder was not set properly on {name}");
                            }
                        }
                        else if (remoteSensor.Source != RemoteSensorSource.Off)
                        {
                            initCode.Add(string.Format(@"   configs.Feedback.FeedbackRemoteSensorID = {1};
                                                    configs.Feedback.FeedbackSensorSource = {2};
                                                    configs.Feedback.SensorToMechanismRatio = {3};",
                                                                                AsMemberVariableName(),
                                                                                remoteSensor.CanID.value,
                                                                                sensorSource,
                                                                                remoteSensor.sensorToMechanismRatio));
                        }
                        else
                        {
                            double SensorToMechanismRatio = theDistanceAngleCalcInfo.isDistance.value ? theDistanceAngleCalcInfo.gearRatio.value / (Math.PI * theDistanceAngleCalcInfo.diameter.value) : theDistanceAngleCalcInfo.gearRatio.value;
                            initCode.Add(string.Format(@"   configs.Feedback.FeedbackSensorSource = FeedbackSensorSourceValue::RotorSensor;
                                                    configs.Feedback.SensorToMechanismRatio = {1};",
                                                            AsMemberVariableName(), SensorToMechanismRatio
                                                            ));
                        }
                    }
                    initCode.Add(Environment.NewLine);

                    initCode.Add(string.Format(@"   ctre::phoenix::StatusCode status = ctre::phoenix::StatusCode::StatusCodeNotInitialized;                                     
                                                for(int i = 0; i < 5; ++i)
                                                {{
                                                    status = {0}->GetConfigurator().Apply(configs, units::time::second_t(0.25));
                                                    if (status.IsOK())
                                                        break;
                                                }}
                                                if (!status.IsOK())
                                                    Logger::GetLogger()->LogData(LOGGER_LEVEL::ERROR, ""{0}"",""{0} Status"", status.GetName());
                                                ",

                                                    AsMemberVariableName()));
                }

                initCode.Add("}");
            }

            return initCode;
        }

        override public List<string> generateIndexedObjectCreation(int currentIndex)
        {
            List<applicationData> robotsToCreateFor = new List<applicationData>();
            List<MotorController> mcs = generatorContext.theMechanism.MotorControllers.FindAll(m => m.name == name);
            applicationData robot = generatorContext.theRobot;

            if (mcs.Count > 1)
            {
                mechanismInstance mi = robot.mechanismInstances.Find(m => m.name == generatorContext.theMechanismInstance.name);
                if (mi != null) // are we using the same mechanism instance in this robot
                {
                    mcs = mi.mechanism.MotorControllers.FindAll(m => (m.ControllerEnabled == MotorController.Enabled.Yes) && (m.name == name) && (m.GetType() == this.GetType()));
                    if (mcs.Count > 1)
                        throw new Exception(string.Format("In robot id {0}, found more than one enabled motor controller named {1}.", robot.robotID, name));
                    if (mcs.Count > 0)
                        robotsToCreateFor.Add(robot);
                }
            }
            else
                robotsToCreateFor.Add(robot);

            if (robotsToCreateFor.Count == 0)
                return new List<string>() { };

            string creation = string.Format("{0} = new {1}({2}, \"{3}\");",
                AsMemberVariableName(),
                getImplementationName(),
                canID.value.ToString(),
                canBusName.ToString());

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(creation);
            sb.AppendLine();

            return new List<string>() { sb.ToString() };
        }

        override public string GenerateTargetMemberVariable(motorControlData mcd)
        {
            string targetNameAsMemVar = mcd.AsMemberVariableName(string.Format("{0}{1}", this.name, mcd.name));
            if (!this.enableFollowID.value)
            {
                if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
                {
                    return string.Format("ctre::phoenix6::controls::DutyCycleOut {0}{{0.0}};", targetNameAsMemVar);
                }
                else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
                {
                    return string.Format("ctre::phoenix6::controls::VoltageOut {0}{{units::voltage::volt_t(0.0)}};", targetNameAsMemVar);
                }

                if (!mcd.enableFOC.value)
                {
                    if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES)
                    {
                        return string.Format("ctre::phoenix6::controls::PositionVoltage {0}{{units::angle::turn_t(0.0)}};", targetNameAsMemVar);
                    }
                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH)
                    {
                        return string.Format("ctre::phoenix6::controls::PositionVoltage {0}{{units::angle::turn_t(0.0)}};", targetNameAsMemVar);
                    }
                }
                else
                {
                    if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES)
                    {
                        return string.Format("ctre::phoenix6::controls::PositionTorqueCurrentFOC {0}{{units::angle::turn_t(0.0)}};", targetNameAsMemVar);
                    }
                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH)
                    {
                        return string.Format("ctre::phoenix6::controls::PositionTorqueCurrentFOC {0}{{units::angle::turn_t(0.0)}};", targetNameAsMemVar);
                    }
                }
            }
            return "";
        }

        override public string GenerateGenericTargetMemberVariable()
        {
            if (!this.enableFollowID.value)
                return string.Format("ctre::phoenix6::controls::ControlRequest *{0}ActiveTarget;", AsMemberVariableName());
            return "";
        }

        override public List<string> GenerateTargetUpdateFunctions(motorControlData mcd)
        {
            List<string> output = new List<string>();

            string targetNameAsMemVar = mcd.AsMemberVariableName(string.Format("{0}{1}", this.name, mcd.name));
            string activeTargetNameAsMemVar = string.Format("{0}ActiveTarget", AsMemberVariableName());
            if (!this.enableFollowID.value)
            {
                if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
                {
                    output.Add(string.Format("void UpdateTarget{0}{1}(double percentOut) {{ {2}.Output = percentOut; {3} = &{2};}}", ToUpperCamelCase(), mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                    output.Add(string.Format("void UpdateTarget{0}{1}(double percentOut, bool enableFOC) {{ {2}.Output = percentOut; {2}.EnableFOC = enableFOC; {3} = &{2};}}", ToUpperCamelCase(), mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                }
                else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
                {
                    output.Add(string.Format("void UpdateTarget{0}{1}(units::voltage::volt_t voltageOut) {{ {2}.Output = voltageOut; {3} = &{2};}}", ToUpperCamelCase(), mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                    output.Add(string.Format("void UpdateTarget{0}{1}(units::voltage::volt_t voltageOut, bool enableFOC) {{ {2}.Output = voltageOut; {2}.EnableFOC = enableFOC; {3} = &{2};}}", ToUpperCamelCase(), mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                }
                else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES)
                {
                    output.Add(string.Format("void UpdateTarget{0}{1}(units::angle::turn_t position) {{ {2}.Position = position; {3} = &{2};}}", ToUpperCamelCase(), mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                }
                else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH)
                {
                    output.Add(string.Format("void UpdateTarget{0}{1}(units::length::inch_t position) {{ {2}.Position = units::angle::turn_t(position.value()); {3} = &{2};}}", ToUpperCamelCase(), mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                }
            }
            return output;

        }
        override public string GenerateTargetUpdateFunctionCall(motorControlData mcd, double value)
        {
            if (!this.enableFollowID.value)
            {
                return string.Format("UpdateTarget{0}{1}(m_{2}Target)", ToUpperCamelCase(), mcd.name, name);
            }
            return "";
        }

        override public string GeneratePIDSetFunction(motorControlData mcd, mechanismInstance mi)
        {
            if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
            {
                return ""; // not closed loop
            }
            else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
            {
                return ""; // not closed loop
            }
            else if (!enableFollowID.value && (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES || mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format("void {2}::SetPID{0}{1}()", ToUpperCamelCase(), mcd.name, mi.name));
                sb.AppendLine("{");
                sb.AppendLine("Slot0Configs slot0Configs{};");
                sb.AppendLine(string.Format("slot0Configs.kP = {0}->GetP();", mcd.AsMemberVariableName()));
                sb.AppendLine(string.Format("slot0Configs.kI = {0}->GetI();", mcd.AsMemberVariableName()));
                sb.AppendLine(string.Format("slot0Configs.kD = {0}->GetD();", mcd.AsMemberVariableName()));

                sb.AppendLine(string.Format("slot0Configs.kG = {0}->GetF();", mcd.AsMemberVariableName()));
                sb.AppendLine(string.Format("slot0Configs.kS = {0}->GetS();", mcd.AsMemberVariableName()));
                sb.AppendLine(string.Format("slot0Configs.kV = {0}->GetV();", mcd.AsMemberVariableName()));
                sb.AppendLine(string.Format("slot0Configs.kA = {0}->GetA();", mcd.AsMemberVariableName()));
               // slot0Configs.GravityType = ctre::phoenix6::signals::GravityTypeValue::Arm_Cosine;
               // slot0Configs.StaticFeedforwardSign = ctre::phoenix6::signals::StaticFeedforwardSignValue(0); // uses Velcoity Sign

                sb.AppendLine(string.Format("{0}->GetConfigurator().Apply(slot0Configs);", AsMemberVariableName()));
                sb.AppendLine("}");

                return sb.ToString();
            }


            return "";
        }

        override public string GeneratePIDSetFunctionDeclaration(motorControlData mcd, mechanismInstance mi)
        {
            if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
            {
                return ""; // not closed loop
            }
            else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
            {
                return ""; // not closed loop
            }
            else if (!enableFollowID.value && (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES || mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH))
            {
                return string.Format("void SetPID{0}{1}()", ToUpperCamelCase(), mcd.name, mi.name);
            }

            return "";
        }

        override public string GeneratePIDSetFunctionCall(motorControlData mcd, mechanismInstance mi)
        {
            string call = GeneratePIDSetFunctionDeclaration(mcd, mi);
            if (string.IsNullOrEmpty(call))
                return "";

            return string.Format("{0};", call.Replace("void ", ""));
        }

        override public string GenerateCyclicGenericTargetRefresh()
        {
            if (enableFollowID.value) return "";
            return string.Format("{0}->SetControl(*{0}ActiveTarget);", AsMemberVariableName());
        }
    }



    [Serializable()]
    [ImplementationName("ctre::phoenix6::hardware::TalonFX")]
    [UserIncludeFile("ctre/phoenix6/TalonFX.hpp")]
    [UserIncludeFile("ctre/phoenix6/controls/Follower.hpp")]
    [UserIncludeFile("ctre/phoenix6/configs/Configs.hpp")]
    [Using("ctre::phoenix6::signals::ForwardLimitSourceValue")]
    [Using("ctre::phoenix6::signals::ForwardLimitTypeValue")]
    [Using("ctre::phoenix6::signals::ReverseLimitSourceValue")]
    [Using("ctre::phoenix6::signals::ReverseLimitTypeValue")]
    [Using("ctre::phoenix6::signals::InvertedValue")]
    [Using("ctre::phoenix6::signals::NeutralModeValue")]
    [Using("ctre::phoenix6::configs::Slot0Configs")]
    [Using("ctre::phoenix6::configs::Slot1Configs")]
    [Using("ctre::phoenix6::configs::TalonFXConfiguration")]
    [Using("ctre::phoenix6::signals::FeedbackSensorSourceValue")]
    public class TalonFX : TalonBase
    {
        public TalonFX()
        {
        }
    }

    [Serializable()]
    [ImplementationName("ctre::phoenix6::hardware::TalonFXS")]
    [UserIncludeFile("ctre/phoenix6/TalonFXS.hpp")]
    [UserIncludeFile("ctre/phoenix6/controls/Follower.hpp")]
    [UserIncludeFile("ctre/phoenix6/configs/Configs.hpp")]
    [Using("ctre::phoenix6::signals::ForwardLimitSourceValue")]
    [Using("ctre::phoenix6::signals::ForwardLimitTypeValue")]
    [Using("ctre::phoenix6::signals::ReverseLimitSourceValue")]
    [Using("ctre::phoenix6::signals::ReverseLimitTypeValue")]
    [Using("ctre::phoenix6::signals::InvertedValue")]
    [Using("ctre::phoenix6::signals::NeutralModeValue")]
    [Using("ctre::phoenix6::configs::Slot0Configs")]
    [Using("ctre::phoenix6::configs::Slot1Configs")]
    [Using("ctre::phoenix6::configs::TalonFXSConfiguration")]
    [Using("ctre::phoenix6::signals::FeedbackSensorSourceValue")]
    public class TalonFXS : TalonBase
    {
        public TalonFXS()
        {
        }
    }

    [Serializable]
    public class FeedbackSensorConfigBase : baseRobotElementClass
    {
        [DefaultValue(0)]
        [Range(typeof(int), "0", "3")]
        [ConstantInMechInstance]
        public intParameter pidSlotId { get; set; }

        [DefaultValue(0)]
        [PhysicalUnitsFamily(physicalUnit.Family.time)]
        [ConstantInMechInstance]
        public doubleParameter timeOut { get; set; }

        public FeedbackSensorConfigBase()
        {
        }
    }

    [Serializable]
    public class FeedbackSensorConfig_SRX : FeedbackSensorConfigBase
    {
        public enum TalonSRXFeedbackDevice
        {
            /**
             * Quadrature encoder
             */
                QuadEncoder = 0,
            //1
            /**
             * Analog potentiometer/encoder
             */
            Analog = 2,
            //3
            /**
             * Tachometer
             */
            Tachometer = 4,
            /**
             * CTRE Mag Encoder in Absolute mode or
             * any other device that uses PWM to encode its output
             */
            PulseWidthEncodedPosition = 8,
            /**
             * Sum0 + Sum1
             */
            SensorSum = 9,
            /**
             * Diff0 - Diff1
             */
            SensorDifference = 10,
            /**
             * Sensor configured in RemoteFilter0
             */
            RemoteSensor0 = 11,
            /**
             * Sensor configured in RemoteFilter1
             */
            RemoteSensor1 = 12,
            //13
            /**
             * Position and velocity will read 0.
             */
            None = 14,
            /**
             * Motor Controller will fake a sensor based on applied motor output.
             */
            SoftwareEmulatedSensor = 15,
            /**
             * CTR mag encoder configured in absolute, is the same
             * as a PWM sensor.
             */
            CTRE_MagEncoder_Absolute = PulseWidthEncodedPosition,
            /**
             * CTR mag encoder configured in relative, is the same
             * as an quadrature encoder sensor.
             */
            CTRE_MagEncoder_Relative = QuadEncoder,
        }

        [ConstantInMechInstance]
        public TalonSRXFeedbackDevice device { get; set; }

        public FeedbackSensorConfig_SRX()
        {
        }
    }

    [Serializable]
    public class RemoteFeedbackSensorConfig_SRX : FeedbackSensorConfigBase
    {
        public enum RemoteFeedbackDevice
        {
            /**
             * Use Sum0 + Sum1
             */
            SensorSum = 9,
            /**
             * Use Diff0 - Diff1
             */
            SensorDifference = 10,

            /**
             * Use the sensor configured
             * in filter0
             */
            RemoteSensor0 = 11,
            /**
             * [[deprecated("Use RemoteSensor1 instead.")]]
             * Use the sensor configured
             * in filter1
             */
            RemoteSensor1 = 12,
            /**
             * Position and velocity will read 0.
             */
            None = 14,
            /**
             * Motor Controller will fake a sensor based on applied motor output.
             */
            SoftwareEmulatedSensor = 15,
        };

        [ConstantInMechInstance]
        public RemoteFeedbackDevice device { get; set; }

        public RemoteFeedbackSensorConfig_SRX()
        {
            device = RemoteFeedbackDevice.None;
        }
    }

    [Serializable]
    public class LimitSwitches : baseDataClass
    {
        [DefaultValue(SwitchConfiguration.NormallyOpen)]
        [ConstantInMechInstance]
        public SwitchConfiguration ForwardLimitSwitch { get; set; }

        [DefaultValue(SwitchConfiguration.NormallyOpen)]
        [ConstantInMechInstance]
        public SwitchConfiguration ReverseLimitSwitch { get; set; }

        [DefaultValue(false)]
        [ConstantInMechInstance]
        public boolParameter LimitSwitchesEnabled { get; set; }

        public LimitSwitches()
        {
            defaultDisplayName = this.GetType().Name;
        }
    }

    [Serializable]
    [ImplementationName("ctre::phoenix::motorcontrol::can::TalonSRX")]
    [SystemIncludeFile("ctre/phoenix/motorcontrol/can/TalonSRX.h")]
    [SystemIncludeFile("ctre/phoenix/motorcontrol/SupplyCurrentLimitConfiguration.h")]
    public class TalonSRX : MotorController
    {

        public LimitSwitches limitSwitches { get; set; }

        [Serializable]
        public class CurrentLimits_SRX : baseDataClass
        {
            [DefaultValue(false)]
            [ConstantInMechInstance]
            public boolParameter EnableCurrentLimits { get; set; }

            [DefaultValue(0)]
            [PhysicalUnitsFamily(physicalUnit.Family.current)]
            [ConstantInMechInstance]
            public intParameter currentLimit { get; set; }

            [DefaultValue(0)]
            [PhysicalUnitsFamily(physicalUnit.Family.time)]
            [ConstantInMechInstance]
            public intParameter triggerThresholdCurrent { get; set; }

            [DefaultValue(0)]
            [PhysicalUnitsFamily(physicalUnit.Family.time)]
            [ConstantInMechInstance]
            public intParameter triggerThresholdTime { get; set; }

            public CurrentLimits_SRX()
            {
                int index = this.GetType().Name.IndexOf("_");
                if (index > 0)
                    defaultDisplayName = this.GetType().Name.Substring(0, index);
                else
                    defaultDisplayName = this.GetType().Name;
            }
        }
        public CurrentLimits_SRX currentLimits { get; set; }

        [Serializable]
        public class ConfigMotorSettings_SRX : baseDataClass
        {
            [DefaultValue(InvertedValue.CounterClockwise_Positive)]
            public InvertedValue inverted { get; set; }

            [DefaultValue(NeutralModeValue.Coast)]
            [ConstantInMechInstance]
            public NeutralModeValue mode { get; set; }

            public ConfigMotorSettings_SRX()
            {
                int index = this.GetType().Name.IndexOf("_");
                if (index > 0)
                    defaultDisplayName = this.GetType().Name.Substring(0, index);
                else
                    defaultDisplayName = this.GetType().Name;
            }
        }
        public ConfigMotorSettings_SRX theConfigMotorSettings { get; set; }

        public List<FeedbackSensorConfig_SRX> feedbackSensorConfig { get; set; }
        public List<RemoteFeedbackSensorConfig_SRX> remoteFeedbackSensorConfig { get; set; }

        public TalonSRX()
        {
        }
        override public List<string> generateDefinition()
        {
            return new List<string> { string.Format("{0} *{1};", getImplementationName(), AsMemberVariableName()) };
        }
        override public List<string> generateInitialization()
        {
            List<string> initCode = new List<string>();

            if (ControllerEnabled == Enabled.Yes)
            {
                string signatureWithoutReturn = string.Format("Initialize{0}{1}$$_ROBOT_FULL_NAME_$$()", this.GetType().Name, name, generatorContext.theMechanismInstance.name);

                initCode.Add(string.Format("CALL:{0}", signatureWithoutReturn));
                initCode.Add(string.Format("DECLARATION:void {0}", signatureWithoutReturn));
                initCode.Add("");
                initCode.Add(string.Format("void {0}::{1}", generatorContext.theMechanismInstance.name, signatureWithoutReturn));
                initCode.Add("{");

                initCode.Add(string.Format("{0}->SetInverted({1});",
                                                                         AsMemberVariableName(),
                                                                         (theConfigMotorSettings.inverted == InvertedValue.CounterClockwise_Positive).ToString().ToLower()));

                initCode.Add(string.Format("{0}->EnableVoltageCompensation(true);", AsMemberVariableName()));

                initCode.Add(string.Format("{0}->ConfigVoltageCompSaturation(10.0, 0);", AsMemberVariableName()));

                initCode.Add(string.Format("{0}->SetNeutralMode(ctre::phoenix::motorcontrol::NeutralMode::{1});",
                                                                      AsMemberVariableName(),
                                                                      theConfigMotorSettings.mode.ToString()));
                initCode.Add(string.Format("{0}->ConfigOpenloopRamp({1});", AsMemberVariableName(), voltageRamping.openLoopRampTime.value));

                initCode.Add(Environment.NewLine);

                if (currentLimits.EnableCurrentLimits.value)
                {
                    initCode.Add(string.Format(@"ctre::phoenix::motorcontrol::SupplyCurrentLimitConfiguration climit;
                                                climit.enable = true;
                                                climit.currentLimit = {0};
                                                climit.triggerThresholdCurrent = {1};
                                                climit.triggerThresholdTime = {2};
                                                {3}->ConfigSupplyCurrentLimit(climit, 0);",
                                                currentLimits.currentLimit.value,
                                                currentLimits.triggerThresholdCurrent.value,
                                                currentLimits.triggerThresholdTime.value,
                                                AsMemberVariableName()));
                }
                initCode.Add("}");
                initCode.Add(Environment.NewLine);
            }
            return initCode;
        }
        override public List<string> GenerateTargetUpdateFunctions(motorControlData mcd)
        {
            List<string> output = new List<string>();

            string targetNameAsMemVar = mcd.AsMemberVariableName(string.Format("{0}{1}", this.name, mcd.name));
            string activeTargetNameAsMemVar = string.Format("{0}ActiveTarget", AsMemberVariableName());

            if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
            {
                output.Add(string.Format("void UpdateTarget{0}{1}(double percentOut)  {{{2} = percentOut;}}", this.name, mcd.name, activeTargetNameAsMemVar));
            }/*TO DO if we use SRX for mor than Percent Out
            else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
            {
                output.Add(string.Format("void UpdateTarget{0}{1}(units::voltage::volt_t voltageOut) {{ {2}.Output = voltageOut; {3} = &{2};}}", this.name, mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
                output.Add(string.Format("void UpdateTarget{0}{1}(units::voltage::volt_t voltageOut, bool enableFOC) {{ {2}.Output = voltageOut; {2}.EnableFOC = enableFOC; {3} = &{2};}}", this.name, mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar));
            }
            else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES)
            {
                output.Add(string.Format("void UpdateTarget{0}{1}(units::angle::turn_t position) {{ {2}.Position = position * {4}; {3} = &{2};}}", this.name, mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar, this.theDistanceAngleCalcInfo.gearRatio));
                output.Add(string.Format("void UpdateTarget{0}{1}(units::angle::turn_t position, bool enableFOC) {{ {2}.Position = position * {4}; {2}.EnableFOC = enableFOC; {3} = &{2};}}", this.name, mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar, this.theDistanceAngleCalcInfo.gearRatio));
            }
            else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH)
            {
                output.Add(string.Format("void UpdateTarget{0}{1}(units::length::inch_t position) {{ {2}.Position = position * {4} / (std::numbers::pi * {5}); {3} = &{2};}}", this.name, mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar, this.theDistanceAngleCalcInfo.gearRatio, this.theDistanceAngleCalcInfo.diameter));
                output.Add(string.Format("void UpdateTarget{0}{1}(units::length::inch_t position, bool enableFOC) {{ {2}.Position = position * {4} / (std::numbers::pi * {5}); {2}.EnableFOC = enableFOC; {3} = &{2};}}", this.name, mcd.name, targetNameAsMemVar, activeTargetNameAsMemVar, this.theDistanceAngleCalcInfo.gearRatio, this.theDistanceAngleCalcInfo.diameter));
            }*/

            return output;
        }
        override public List<string> generateIndexedObjectCreation(int currentIndex)
        {
            List<applicationData> robotsToCreateFor = new List<applicationData>();
            List<MotorController> mcs = generatorContext.theMechanism.MotorControllers.FindAll(m => m.name == name);
            applicationData robot = generatorContext.theRobot;
            if (mcs.Count > 1)
            {
                mechanismInstance mi = robot.mechanismInstances.Find(m => m.name == generatorContext.theMechanismInstance.name);
                if (mi != null) // are we using the same mechanism instance in this robot
                {
                    mcs = mi.mechanism.MotorControllers.FindAll(m => (m.ControllerEnabled == MotorController.Enabled.Yes) && (m.name == name) && (m.GetType() == this.GetType()));
                    if (mcs.Count > 1)
                        throw new Exception(string.Format("In robot id {0}, found more than one enabled motor controller named {1}.", robot.robotID, name));
                    if (mcs.Count > 0)
                        robotsToCreateFor.Add(robot);
                }
            }
            else
                robotsToCreateFor.Add(robot);

            if (robotsToCreateFor.Count == 0)
                return new List<string>() { };

            string creation = string.Format("{0} = new {1}({2});",
                AsMemberVariableName(),
                getImplementationName(),
                canID.value.ToString());

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(creation);
            sb.AppendLine();

            return new List<string>() { sb.ToString() };
        }
        override public string GenerateTargetMemberVariable(motorControlData mcd)
        {
            string targetNameAsMemVar = mcd.AsMemberVariableName(string.Format("{0}{1}", this.name, mcd.name));

            if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
            {
                return string.Format("double  {0}ActiveTarget;", AsMemberVariableName());
            }
            /* //TO DO if we need more than Percent Out implement below

             else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
             {
                 return string.Format("ctre::phoenix6::controls::VoltageOut {0}{{units::voltage::volt_t(0.0)}};", targetNameAsMemVar);
             }
             else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES)
             {
                 return string.Format("ctre::phoenix6::controls::PositionVoltage {0}{{units::angle::turn_t(0.0)}};", targetNameAsMemVar);
             }*/

            return "";
        }
        override public string GenerateTargetUpdateFunctionCall(motorControlData mcd, double value)
        {
            if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
            {
                return string.Format("UpdateTarget{0}{1}(m_{2}Target)", ToUpperCamelCase(), mcd.name, name);
            }

            /*TO DO if we need more than Percent Out implement below
             else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT)
             {
                 return string.Format("UpdateTarget{0}{1}(units::voltage::volt_t({2}), {3})", this.name, mcd.name, value, mcd.enableFOC);
             }
             else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES)
             {
                 return string.Format("UpdateTarget{0}{1}(units::angle::turn_t({2}), {3})", this.name, mcd.name, value, mcd.enableFOC);
             }*/

            return "";
        }
        override public string GenerateCyclicGenericTargetRefresh()
        {
            return string.Format("{0}->Set(ctre::phoenix::motorcontrol::TalonSRXControlMode::PercentOutput,{0}ActiveTarget);", AsMemberVariableName());
        }
    }



    [Serializable]
    [NotUserAddable]
    public class SparkController : MotorController
    {
        public enum Type { kBrushed = 0, kBrushless = 1 };
        public enum SensorType { kNoSensor = 0, kHallSensor = 1, kQuadrature = 2 }
        public enum ControlType
        {
            kDutyCycle = 0,
            kVelocity = 1,
            kVoltage = 2,
            kPosition = 3,
            kSmartMotion = 4,
            kCurrent = 5,
            kSmartVelocity = 6
        }

        public enum ParameterStatus
        {
            kOK = 0,
            kInvalidID = 1,
            kMismatchType = 2,
            kAccessMode = 3,
            kInvalid = 4,
            kNotImplementedDeprecated = 5,
        }

        public enum PeriodicFrame
        {
            kStatus0 = 0,
            kStatus1 = 1,
            kStatus2 = 2,
            kStatus3 = 3,
            kStatus4 = 4,
            kStatus5 = 5,
            kStatus6 = 6,
            kStatus7 = 7,
        }

        public LimitSwitches limitSwitches { get; set; }

        [Serializable]
        public class CurrentLimits_SparkController : baseDataClass
        {
            [DefaultValue(50)]
            [PhysicalUnitsFamily(physicalUnit.Family.current)]
            [ConstantInMechInstance]
            [Range(0, 100)]
            public intParameter PrimaryLimit { get; set; }

            [DefaultValue(50)]
            [PhysicalUnitsFamily(physicalUnit.Family.current)]
            [ConstantInMechInstance]
            [Range(0, 100)]
            public intParameter SecondaryLimit { get; set; }

            [DefaultValue(0)]
            [ConstantInMechInstance]
            public intParameter SecondaryLimitCycles { get; set; }

            public CurrentLimits_SparkController()
            {
                int index = this.GetType().Name.IndexOf("_");
                if (index > 0)
                    defaultDisplayName = this.GetType().Name.Substring(0, index);
                else
                    defaultDisplayName = this.GetType().Name;
            }
        }
        public CurrentLimits_SparkController currentLimits { get; set; }

        [Serializable]
        public class ConfigMotorSettings_SparkController : baseDataClass
        {
            [DefaultValue(InvertedValue.CounterClockwise_Positive)]
            public InvertedValue inverted { get; set; }

            [DefaultValue(NeutralModeValue.Coast)]
            [ConstantInMechInstance]
            public NeutralModeValue mode { get; set; }

            public ConfigMotorSettings_SparkController()
            {
                int index = this.GetType().Name.IndexOf("_");
                if (index > 0)
                    defaultDisplayName = this.GetType().Name.Substring(0, index);
                else
                    defaultDisplayName = this.GetType().Name;
            }
        }
        public ConfigMotorSettings_SparkController theConfigMotorSettings { get; set; }

        [DefaultValue(1.0)]
        [ConstantInMechInstance]
        public doubleParameter RotationOffset { get; set; }


        public Type motorBrushType { get; set; }

        public SensorType sensorType { get; set; }
    }


    [Serializable]
    [ImplementationName("DragonSparkMax")]
    [UserIncludeFile("hw/DragonSparkMax.h")]
    public class SparkMax : SparkController
    {
        override public List<string> generateIndexedObjectCreation(int currentIndex)
        {
            List<applicationData> robotsToCreateFor = new List<applicationData>();
            List<MotorController> mcs = generatorContext.theMechanism.MotorControllers.FindAll(m => m.name == name);
            if (mcs.Count > 1)
            {
                foreach (applicationData robot in generatorContext.theRobotVariants.Robots)
                {
                    mechanismInstance mi = robot.mechanismInstances.Find(m => m.name == generatorContext.theMechanismInstance.name);
                    if (mi != null) // are we using the same mechanism instance in this robot
                    {
                        mcs = mi.mechanism.MotorControllers.FindAll(m => (m.ControllerEnabled == MotorController.Enabled.Yes) && (m.name == name) && (m.GetType() == this.GetType()));
                        if (mcs.Count > 1)
                            throw new Exception(string.Format("In robot id {0}, found more than one enabled motor controller named {1}.", robot.robotID, name));
                        if (mcs.Count > 0)
                            robotsToCreateFor.Add(robot);
                    }
                }
            }

            StringBuilder conditionalsSb = new StringBuilder();
            if (robotsToCreateFor.Count > 0)
            {
                conditionalsSb.Append("if(");
                foreach (applicationData r in robotsToCreateFor)
                {
                    conditionalsSb.Append("(RobotIdentifier::");
                    conditionalsSb.Append(string.Format("{0}_{1}", ToUnderscoreCase(r.name).ToUpper(), r.robotID));
                    conditionalsSb.Append(" == m_activeRobotId)");
                    if (r != robotsToCreateFor.Last())
                        conditionalsSb.Append(" || ");
                }
                conditionalsSb.Append(")");
            }

            string creation = string.Format("{0}{1} = new {1}({2},RobotElementNames::{3},rev::CANSparkMax::MotorType::{4},rev::SparkRelativeEncoder::Type::{5},rev::SparkLimitSwitch::Type::k{7},rev::SparkLimitSwitch::Type::k{8},{6});",
                name,
                getImplementationName(),
                canID.value.ToString(),
                utilities.ListToString(generateElementNames()).ToUpper().Replace("::", "_USAGE::"),
                motorBrushType,
                sensorType,
                theDistanceAngleCalcInfo.getName(name),
                limitSwitches.ForwardLimitSwitch,
                limitSwitches.ReverseLimitSwitch);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(conditionalsSb.ToString());
            if (robotsToCreateFor.Count > 0)
                sb.AppendLine("{");
            sb.AppendLine(theDistanceAngleCalcInfo.getDefinition(name));
            sb.AppendLine(creation);
            sb.AppendLine();
            sb.AppendLine(ListToString(generateObjectAddToMaps(), ";", true));
            if (robotsToCreateFor.Count > 0)
                sb.AppendLine("}");

            return new List<string>() { sb.ToString() };
        }

        override public List<string> generateInitialization()
        {
            List<string> initCode = new List<string>();

            if (ControllerEnabled == Enabled.Yes)
            {
                initCode.Add(string.Format(@"{0}->SetRemoteSensor({1},
                                                              ctre::phoenix::motorcontrol::{2}::{2}_{3} );",
                                                                        name + getImplementationName(),
                                                                        remoteSensor.CanID.value,
                                                                        remoteSensor.Source.GetType().Name,
                                                                        remoteSensor.Source
                                                                        ));

                initCode.Add(string.Format("{0}->Invert( {1});",
                                                                        name + getImplementationName(),
                                                                        (theConfigMotorSettings.inverted == InvertedValue.CounterClockwise_Positive).ToString().ToLower()));

                initCode.Add(string.Format("{0}->EnableBrakeMode( {1});",
                                                                        name + getImplementationName(),
                                                                        (theConfigMotorSettings.mode == NeutralModeValue.Brake).ToString().ToLower()));

                initCode.Add(string.Format("{0}->SetSmartCurrentLimiting({1});",
                                                                        name + getImplementationName(),
                                                                        currentLimits.PrimaryLimit.value));

                initCode.Add(string.Format("{0}->SetSecondaryCurrentLimiting({1}, {2});",
                                                                        name + getImplementationName(),
                                                                        currentLimits.SecondaryLimit.value,
                                                                        currentLimits.SecondaryLimitCycles.value));

                //initCode.Add(string.Format(@"{0}->ConfigPeakCurrentLimit(units::current::ampere_t ( {1}({2})).to<int>(), 
                //                                                         units::time::millisecond_t({3}({4})).to<int>() );",      //todo check return code
                //                                                        name,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentLimit.physicalUnits),
                //                                                        currentLimits.PeakCurrentLimit.value,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentLimitTimeout.physicalUnits),
                //                                                        currentLimits.PeakCurrentLimitTimeout.value));

                //initCode.Add(string.Format(@"{0}->ConfigPeakCurrentDuration(units::time::millisecond_t ( {1}({2})).to<int>(), 
                //                                                            units::time::millisecond_t({3}({4})).to<int>() );",      //todo check return code
                //                                                        name,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentDuration.physicalUnits),
                //                                                        currentLimits.PeakCurrentDuration.value,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentDurationTimeout.physicalUnits),
                //                                                        currentLimits.PeakCurrentDurationTimeout.value));

                //initCode.Add(string.Format(@"{0}->ConfigContinuousCurrentLimit(units::current::ampere_t ( {1}({2})).to<int>(), 
                //                                                               units::time::millisecond_t({3}({4})).to<int>() );",      //todo check return code
                //                                                        name,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.ContinuousCurrentLimit.physicalUnits),
                //                                                        currentLimits.ContinuousCurrentLimit.value,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.ContinuousCurrentLimitTimeout.physicalUnits),
                //                                                        currentLimits.ContinuousCurrentLimitTimeout.value));

                initCode.Add(string.Format("{0}->SetDiameter(units::length::inch_t ( {1}({2})).to<double>());",      //todo Should SetDiameter(double) be called within the constructor, since the diameter is inside the calcStruct that is passed to the constructor?
                                                                        name + getImplementationName(),
                                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theDistanceAngleCalcInfo.diameter.physicalUnits),
                                                                        theDistanceAngleCalcInfo.diameter.value));

                initCode.Add(string.Format("{0}->EnableDisableLimitSwitches( {1});",
                                                                        name + getImplementationName(),
                                                                        limitSwitches.LimitSwitchesEnabled.value.ToString().ToLower()));


                if (enableFollowID.value)
                {
                    initCode.Add(string.Format("{0}->SetAsFollowerMotor( {1} );",
                                                                            name,
                                                                            followID.value));
                }
                else
                    initCode.Add(string.Format("// {0} : Follower motor mode is not enabled", name));

                initCode.AddRange(base.generateInitialization());

                initCode.Add(Environment.NewLine);
            }

            return initCode;
        }
    }

    [Serializable]
    [ImplementationName("DragonSparkFlex")]
    [UserIncludeFile("hw/DragonSparkFlex.h")]
    public class SparkFlex : SparkController
    {
        override public List<string> generateIndexedObjectCreation(int currentIndex)
        {
            List<applicationData> robotsToCreateFor = new List<applicationData>();
            List<MotorController> mcs = generatorContext.theMechanism.MotorControllers.FindAll(m => m.name == name);
            if (mcs.Count > 1)
            {
                foreach (applicationData robot in generatorContext.theRobotVariants.Robots)
                {
                    mechanismInstance mi = robot.mechanismInstances.Find(m => m.name == generatorContext.theMechanismInstance.name);
                    if (mi != null) // are we using the same mechanism instance in this robot
                    {
                        mcs = mi.mechanism.MotorControllers.FindAll(m => (m.ControllerEnabled == MotorController.Enabled.Yes) && (m.name == name) && (m.GetType() == this.GetType()));
                        if (mcs.Count > 1)
                            throw new Exception(string.Format("In robot id {0}, found more than one enabled motor controller named {1}.", robot.robotID, name));
                        if (mcs.Count > 0)
                            robotsToCreateFor.Add(robot);
                    }
                }
            }

            StringBuilder conditionalsSb = new StringBuilder();
            if (robotsToCreateFor.Count > 0)
            {
                conditionalsSb.Append("if(");
                foreach (applicationData r in robotsToCreateFor)
                {
                    conditionalsSb.Append("(RobotIdentifier::");
                    conditionalsSb.Append(string.Format("{0}_{1}", ToUnderscoreCase(r.name).ToUpper(), r.robotID));
                    conditionalsSb.Append(" == m_activeRobotId)");
                    if (r != robotsToCreateFor.Last())
                        conditionalsSb.Append(" || ");
                }
                conditionalsSb.Append(")");
            }

            string creation = string.Format("{0}{1} = new {1}({2},RobotElementNames::{3},rev::CANSparkFlex::MotorType::{4},rev::SparkRelativeEncoder::Type::{5},rev::SparkLimitSwitch::Type::k{7},rev::SparkLimitSwitch::Type::k{8},{6});",
                name,
                getImplementationName(),
                canID.value.ToString(),
                utilities.ListToString(generateElementNames()).ToUpper().Replace("::", "_USAGE::"),
                motorBrushType,
                sensorType,
                theDistanceAngleCalcInfo.getName(name),
                limitSwitches.ForwardLimitSwitch,
                limitSwitches.ReverseLimitSwitch);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(conditionalsSb.ToString());
            if (robotsToCreateFor.Count > 0)
                sb.AppendLine("{");
            sb.AppendLine(theDistanceAngleCalcInfo.getDefinition(name));
            sb.AppendLine(creation);
            sb.AppendLine();
            sb.AppendLine(ListToString(generateObjectAddToMaps(), ";", true));
            if (robotsToCreateFor.Count > 0)
                sb.AppendLine("}");

            return new List<string>() { sb.ToString() };
        }

        override public List<string> generateInitialization()
        {
            List<string> initCode = new List<string>();

            if (ControllerEnabled == Enabled.Yes)
            {
                initCode.Add(string.Format(@"{0}->SetRemoteSensor({1},
                                                              ctre::phoenix::motorcontrol::{2}::{2}_{3} );",
                                                                        name + getImplementationName(),
                                                                        remoteSensor.CanID.value,
                                                                        remoteSensor.Source.GetType().Name,
                                                                        remoteSensor.Source
                                                                        ));

                initCode.Add(string.Format("{0}->Invert( {1});",
                                                                        name + getImplementationName(),
                                                                        (theConfigMotorSettings.inverted == InvertedValue.CounterClockwise_Positive).ToString().ToLower()));

                initCode.Add(string.Format("{0}->EnableBrakeMode( {1});",
                                                                        name + getImplementationName(),
                                                                        (theConfigMotorSettings.mode == NeutralModeValue.Brake).ToString().ToLower()));

                initCode.Add(string.Format("{0}->SetSmartCurrentLimiting({1});",
                                                                        name + getImplementationName(),
                                                                        currentLimits.PrimaryLimit.value));

                initCode.Add(string.Format("{0}->SetSecondaryCurrentLimiting({1}, {2});",
                                                                        name + getImplementationName(),
                                                                        currentLimits.SecondaryLimit.value,
                                                                        currentLimits.SecondaryLimitCycles.value));

                //initCode.Add(string.Format(@"{0}->ConfigPeakCurrentLimit(units::current::ampere_t ( {1}({2})).to<int>(), 
                //                                                         units::time::millisecond_t({3}({4})).to<int>() );",      //todo check return code
                //                                                        name,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentLimit.physicalUnits),
                //                                                        currentLimits.PeakCurrentLimit.value,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentLimitTimeout.physicalUnits),
                //                                                        currentLimits.PeakCurrentLimitTimeout.value));

                //initCode.Add(string.Format(@"{0}->ConfigPeakCurrentDuration(units::time::millisecond_t ( {1}({2})).to<int>(), 
                //                                                            units::time::millisecond_t({3}({4})).to<int>() );",      //todo check return code
                //                                                        name,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentDuration.physicalUnits),
                //                                                        currentLimits.PeakCurrentDuration.value,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.PeakCurrentDurationTimeout.physicalUnits),
                //                                                        currentLimits.PeakCurrentDurationTimeout.value));

                //initCode.Add(string.Format(@"{0}->ConfigContinuousCurrentLimit(units::current::ampere_t ( {1}({2})).to<int>(), 
                //                                                               units::time::millisecond_t({3}({4})).to<int>() );",      //todo check return code
                //                                                        name,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.ContinuousCurrentLimit.physicalUnits),
                //                                                        currentLimits.ContinuousCurrentLimit.value,
                //                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(currentLimits.ContinuousCurrentLimitTimeout.physicalUnits),
                //                                                        currentLimits.ContinuousCurrentLimitTimeout.value));

                initCode.Add(string.Format("{0}->SetDiameter(units::length::inch_t ( {1}({2})).to<double>());",      //todo Should SetDiameter(double) be called within the constructor, since the diameter is inside the calcStruct that is passed to the constructor?
                                                                        name + getImplementationName(),
                                                                        generatorContext.theGeneratorConfig.getWPIphysicalUnitType(theDistanceAngleCalcInfo.diameter.physicalUnits),
                                                                        theDistanceAngleCalcInfo.diameter.value));

                initCode.Add(string.Format("{0}->EnableDisableLimitSwitches( {1});",
                                                                        name + getImplementationName(),
                                                                        limitSwitches.LimitSwitchesEnabled.value.ToString().ToLower()));


                if (enableFollowID.value)
                {
                    initCode.Add(string.Format("{0}->SetAsFollowerMotor( {1} );",
                                                                            name,
                                                                            followID.value));
                }
                else
                    initCode.Add(string.Format("// {0} : Follower motor mode is not enabled", name));

                initCode.AddRange(base.generateInitialization());

                initCode.Add(Environment.NewLine);
            }

            return initCode;
        }
    }

    [Serializable]
    [ImplementationName("DragonSparkFlexMonitored")]
    [UserIncludeFile("hw/DragonSparkFlexMonitored.h")]
    public class SparkFlexMonitored : SparkFlex
    {
        [DefaultValue(7u)]
        public uintParameter CurrentFilterLength { get; set; }

        override public List<string> generateInitialization()
        {
            List<string> initCode = base.generateInitialization();

            if (ControllerEnabled == Enabled.Yes)
            {
                initCode.Add(string.Format("{0}->ConfigureCurrentFiltering( {1});",
                                                                        name + getImplementationName(),
                                                                        CurrentFilterLength.value.ToString().ToLower()));
            }

            return initCode;
        }
    }

    [Serializable]
    [ImplementationName("DragonSparkMaxMonitored")]
    [UserIncludeFile("hw/DragonSparkMaxMonitored.h")]
    public class SparkMaxMonitored : SparkMax
    {
        [DefaultValue(7u)]
        public uintParameter CurrentFilterLength { get; set; }

        override public List<string> generateInitialization()
        {
            List<string> initCode = base.generateInitialization();

            if (ControllerEnabled == Enabled.Yes)
            {
                initCode.Add(string.Format("{0}->ConfigureCurrentFiltering( {1});",
                                                                        name + getImplementationName(),
                                                                        CurrentFilterLength.value.ToString().ToLower()));
            }

            return initCode;
        }
    }
}
