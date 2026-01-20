using applicationConfiguration;
using ApplicationData;
using Configuration;
using DataConfiguration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
//using ApplicationData.motorControlData;

namespace CoreCodeGenerator
{
    internal class MechanismInstanceGenerator : baseGenerator
    {
        Dictionary<motorControlData.CONTROL_TYPE, string> ControlDataMapping = new Dictionary<motorControlData.CONTROL_TYPE, string>();
        internal MechanismInstanceGenerator(string codeGeneratorVersion, applicationDataConfig theRobotConfiguration, toolConfiguration theToolConfiguration, bool cleanMode, bool cleanDecoratorModFolders, showMessage displayProgress)
        : base(codeGeneratorVersion, theRobotConfiguration, theToolConfiguration, cleanMode, cleanDecoratorModFolders)
        {
            setProgressCallback(displayProgress);
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.PERCENT_OUTPUT, "double");
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.VOLTAGE_OUTPUT, "units::voltage::volt_t");
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.POSITION_DEGREES, "units::angle::degree_t");
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.POSITION_INCH, "units::length::inch_t");
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.VELOCITY_FEET_PER_SEC, "units::velocity::feet_per_second_t");
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.VELOCITY_REV_PER_SEC, "units::angular_velocity::turns_per_second_t");
            ControlDataMapping.Add(motorControlData.CONTROL_TYPE.VELOCITY_DEGREES_PER_SEC, "units::velocity::degrees_per_second_t");
        }

        public void WriteMechanismParameterFiles()
        {
            foreach (applicationData robot in theRobotConfiguration.theRobotVariants.Robots)
            {
                foreach (mechanismInstance mi in robot.mechanismInstances)
                {
                    string report = mi.SerializeAdjustableParametersToXml(Path.Combine(getDeployOutputPath(), Path.Combine(robot.robotID.value.ToString(), "mechanisms")));
                    addProgress("Wrote " + report);
                }
            }
        }

        public List<string> GetMechanismParameterFileList(uint robotID)
        {
            List<string> files = new List<string>();

            applicationData robot = theRobotConfiguration.theRobotVariants.Robots.Find(r => r.robotID.value == robotID);

            if (robot is null)
                throw new Exception("Cannot find a robot with ID " + robotID);

            foreach (mechanismInstance mi in robot.mechanismInstances)
            {
                string fullFilePath = mi.GetAdjustableParametersXmlFullFilePath(Path.Combine(getDeployOutputPath(), Path.Combine(robot.robotID.value.ToString(), "mechanisms")));
                files.Add(fullFilePath);
            }

            return files;
        }

        internal void generate()
        {
            addProgress((cleanMode ? "Erasing" : "Writing") + " mechanism instance files...");

            List<string> mechMainFiles = new List<string>();
            List<string> mechInstanceNames = new List<string>();
            foreach (applicationData robot in theRobotConfiguration.theRobotVariants.Robots)
            {
                generatorContext.theRobotVariants = theRobotConfiguration.theRobotVariants;
                generatorContext.theRobot = robot;

                List<mechanismInstance> mechInstances = new List<mechanismInstance>();
                mechInstances.AddRange(robot.mechanismInstances);

                int index = 0;
                foreach (mechanismInstance mi in mechInstances)
                {
                    generatorContext.generationStage = generatorContext.GenerationStage.MechInstanceGenCpp;

                    if (!mechInstanceNames.Exists(n => n == mi.name))
                    {
                        mechInstanceNames.Add(mi.name);

                        generatorContext.theMechanismInstance = mi;
                        generatorContext.theMechanism = mi.mechanism;

                        string filePathName;
                        string resultString;
                        codeTemplateFile cdf;
                        string template;

                        string mechanismName = mi.name;


                        createMechanismFolder(mechanismName, true);
                        if (index < robot.mechanismInstances.Count)
                            createMechanismFolder(mechanismName, false);

                        #region Generate Cpp File
                        cdf = theToolConfiguration.getTemplateInfo("MechanismInstance_cpp");
                        template = loadTemplate(cdf.templateFilePathName);

                        resultString = template;

                        #region clean up unused stuff
                        if (index < robot.mechanismInstances.Count)
                        {
                            resultString = resultString.Replace("_STATE_MANAGER_START_", "");
                            resultString = resultString.Replace("_STATE_MANAGER_END_", "");
                        }
                        else
                            resultString = Remove(resultString, "_STATE_MANAGER_START_", "_STATE_MANAGER_END_");
                        #endregion

                        string createFunctionDeclarations;
                        resultString = resultString.Replace("$$_CREATE_FUNCTIONS_$$", GenerateCreateFunctions(mi, out createFunctionDeclarations));

                        resultString = CleanMotorMechanismCode(mi, resultString);
                        resultString = CleanSolenoidMechanismCode(mi, resultString);
                        resultString = CleanServoMechanismCode(mi, resultString);
                        resultString = CleanNtTuningMechanismCode(mi, resultString);

                        string publicInitializationFunctionDeclarations;
                        string privateInitializationFunctionDeclarations;
                        resultString = resultString.Replace("$$_INITIALZATION_FUNCTIONS_$$", GenerateInitializationFunctions(mi, out publicInitializationFunctionDeclarations, out privateInitializationFunctionDeclarations));

                        resultString = resultString.Replace("$$_MECHANISM_TYPE_NAME_$$", ToUnderscoreCase(mi.name).ToUpper());
                        resultString = resultString.Replace("$$_MECHANISM_NAME_$$", mi.mechanism.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_UPPER_CASE_$$", ToUnderscoreCase(mi.name).ToUpper());


                        List<string> theUsings = generateMethod(mi, "generateUsings").Distinct().OrderBy(m => m).ToList();
                        resultString = resultString.Replace("$$_USING_DIRECTIVES_$$", ListToString(theUsings, ";").Trim());

                        resultString = resultString.Replace("$$_INCLUDE_FILES_$$", ListToString(generateMethod(mi.mechanism, "generateIncludes").Distinct().ToList()));

                        List<string> enumMapList = new List<string>();
                        foreach (state s in mi.mechanism.states)
                        {
                            enumMapList.Add(String.Format("{{\"STATE_{0}\", {1}::STATE_NAMES::STATE_{0}}}", ToUnderscoreCase(s.name).ToUpper(), mi.name));
                        }
                        resultString = resultString.Replace("$$_STATE_MAP_$$", ListToString(enumMapList, ",").Trim());

                        #region Tunable Parameters
                        string allParameterReading = "";
                        string allParameterWriting = "";

                        foreach (motorControlData mcd in mi.mechanism.stateMotorControlData)
                        {
                            if (mcd.controlType != motorControlData.CONTROL_TYPE.PERCENT_OUTPUT)
                            {
                                object obj = mcd.PID;
                                Type objType = obj.GetType();

                                PropertyInfo[] propertyInfos = objType.GetProperties();

                                foreach (PropertyInfo pi in propertyInfos)
                                {
                                    bool skip = (pi.Name == "name");
                                    if (!skip)
                                    {
                                        string setItem = pi.Name == "iZone" ? "IZone" : pi.Name.Replace("Gain", "").ToUpper();
                                        allParameterReading += string.Format("{0}->Set{4}( m_table.get()->GetNumber(\"{5}_{1}\", {2}));{3}", mcd.AsMemberVariableName(), pi.Name, pi.GetValue(obj), Environment.NewLine, setItem, mcd.name);
                                        allParameterWriting += string.Format("m_table.get()->PutNumber(\"{5}_{1}\", {0}->Get{4}());{3}", mcd.AsMemberVariableName(), pi.Name, pi.GetValue(obj), Environment.NewLine, setItem, mcd.name);
                                    }
                                }

                            }
                        }

                        // Serialize the controlData parameters to xml
                        mi.SerializeAdjustableParametersToXml(Path.Combine(getDeployOutputPath(), Path.Combine(robot.robotID.value.ToString(), "mechanisms")));

                        // Create conversion from motorControlData string name to the member variable
                        StringBuilder mcdConv = new StringBuilder();
                        foreach (motorControlData mcd in mi.mechanism.stateMotorControlData)
                        {
                            mcdConv.AppendLine(string.Format("if (name.compare(\"{0}\") == 0)", mcd.name));
                            mcdConv.AppendLine(string.Format("return {0};", mcd.AsMemberVariableName()));
                        }

                        resultString = resultString.Replace("$$_CONTROLDATA_NAME_TO_VARIABLE_$$", mcdConv.ToString());

                        resultString = resultString.Replace("$$_READ_TUNABLE_PARAMETERS_$$", allParameterReading);
                        resultString = resultString.Replace("$$_PUSH_TUNABLE_PARAMETERS_$$", allParameterWriting);


                        #endregion

                        #region Data Logging
                        List<string> loggingInitialization = new List<string>();
                        List<string> DataLogDefinition = new List<string>();


                        DataLogDefinition.Add("auto currTime = m_powerTimer.Get();");
                        loggingInitialization.Add(string.Format("m_{0}TotalEnergyLogEntry = wpi::log::DoubleLogEntry(log, \"mechanisms/{0}/TotalEnergy\");", mi.name));
                        loggingInitialization.Add(string.Format("m_{0}TotalEnergyLogEntry.Append(0.0);", mi.name));

                        loggingInitialization.Add(string.Format("m_{0}TotalWattHoursLogEntry = wpi::log::DoubleLogEntry(log, \"mechanisms/{0}/TotalWattHours\");", mi.name));
                        loggingInitialization.Add(string.Format("m_{0}TotalWattHoursLogEntry.Append(0.0);", mi.name));

                        foreach (MotorController mc in mi.mechanism.MotorControllers)
                        {
                            loggingInitialization.Add(string.Format("m_{0}LogEntry = wpi::log::DoubleLogEntry(log, \"mechanisms/{1}/{0}Position\");", mc.name, mi.name));
                            loggingInitialization.Add(string.Format("m_{0}LogEntry.Append(0.0);", mc.name));

                            loggingInitialization.Add(string.Format("m_{0}TargetLogEntry = wpi::log::DoubleLogEntry(log, \"mechanisms/{1}/{0}Target\");", mc.name, mi.name));
                            loggingInitialization.Add(string.Format("m_{0}TargetLogEntry.Append(0.0);", mc.name));

                            loggingInitialization.Add(string.Format("m_{0}PowerLogEntry = wpi::log::DoubleLogEntry(log, \"mechanisms/{1}/{0}Power\");", mc.name, mi.name));
                            loggingInitialization.Add(string.Format("m_{0}PowerLogEntry.Append(0.0);", mc.name));                                                                      //Move these all to a function outside of this later
                                                                                                                                        
                            loggingInitialization.Add(string.Format("m_{0}EnergyLogEntry = wpi::log::DoubleLogEntry(log, \"mechanisms/{1}/{0}Energy\");", mc.name, mi.name));
                            loggingInitialization.Add(string.Format("m_{0}EnergyLogEntry.Append(0.0);", mc.name));

                            DataLogDefinition.Add(string.Format("Log{0}(timestamp, m_{0}->GetPosition().GetValueAsDouble());", mc.name));
                            DataLogDefinition.Add(string.Format("auto {0}Power = DragonPower::CalcPowerEnergy(currTime, m_{0}->GetSupplyVoltage().GetValueAsDouble(), m_{0}->GetSupplyCurrent().GetValueAsDouble());", mc.name));
                            DataLogDefinition.Add(string.Format("m_power = get<0>({0}Power);", mc.name));
                            DataLogDefinition.Add(string.Format("m_energy = get<1>({0}Power);", mc.name));
                            DataLogDefinition.Add("m_totalEnergy += m_energy;");
                            DataLogDefinition.Add(string.Format("Log{0}Power(timestamp, m_power);", mc.name));
                            DataLogDefinition.Add(string.Format("Log{0}Energy(timestamp, m_energy);", mc.name));

                        }
                        foreach (digitalInput di in mi.mechanism.digitalInput)
                        {
                            loggingInitialization.Add(string.Format("m_{0}LogEntry = wpi::log::BooleanLogEntry(log, \"mechanisms/{1}/{0}\");", di.name, mi.name));
                            loggingInitialization.Add(string.Format("m_{0}LogEntry.Append(false);", di.name));
                            loggingInitialization.Add("");
                                                                                                                                             //move all of these too 
                            DataLogDefinition.Add(string.Format("Log{0}(timestamp, Get{0}());", di.name));              
                        }
                        loggingInitialization.Add(string.Format("m_{0}StateLogEntry = wpi::log::IntegerLogEntry(log, \"mechanisms/{0}/{1}\");", mi.name, "State"));
                        loggingInitialization.Add(string.Format("m_{0}StateLogEntry.Append(0);", mi.name));

                        DataLogDefinition.Add(string.Format("Log{0}State(timestamp, GetCurrentState());", mi.name));
                        DataLogDefinition.Add("m_totalWattHours += DragonPower::ConvertEnergyToWattHours(m_totalEnergy);");
                        DataLogDefinition.Add(string.Format("Log{0}TotalEnergy(timestamp, m_totalEnergy);", mi.name));
                        DataLogDefinition.Add(string.Format("Log{0}TotalWattHours(timestamp, m_totalWattHours);", mi.name));
                        DataLogDefinition.Add("m_powerTimer.Reset();");
                        DataLogDefinition.Add("m_powerTimer.Start();");

                        resultString = resultString.Replace("$$_DATA_LOGGING_INITIALIZATION_$$", ListToString(loggingInitialization.Distinct().ToList()));
                        resultString = resultString.Replace("$$_DATALOG_METHOD_$$", ListToString(DataLogDefinition.ToList()));

                        #endregion

                        List<string> targetRefreshCalls = new List<string>();
                        foreach (MotorController mc in mi.mechanism.MotorControllers)
                        {
                            //if (mc.ControllerEnabled == MotorController.Enabled.Yes)
                            {
                                List<string> robotsWithMotorControllerEnabled = new List<string>();
                                List<string> robotsWithMotorControllerDisabled = new List<string>();

                                #region check if the motor is disabled in another robot
                                foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
                                {
                                    mechanismInstance mechanismInstance = r.mechanismInstances.Find(i => i.mechanism.GUID == mi.mechanism.GUID);
                                    if (mechanismInstance != null)
                                    {
                                        MotorController motorCtrl = mechanismInstance.mechanism.MotorControllers.Find((m => (m.name == mc.name) && (m.motorControllerType == mc.motorControllerType) ));
                                        if (motorCtrl != null)
                                        {
                                            if (motorCtrl.ControllerEnabled == MotorController.Enabled.Yes)
                                                robotsWithMotorControllerEnabled.Add(ToUnderscoreDigit(ToUnderscoreCase(r.getFullRobotName())).ToUpper());
                                            else
                                                robotsWithMotorControllerDisabled.Add(ToUnderscoreDigit(ToUnderscoreCase(r.getFullRobotName())).ToUpper());
                                        }
                                    }
                                }
                                #endregion

                                
                                if (robotsWithMotorControllerDisabled.Count() > 0)
                                {
                                    foreach(string s in robotsWithMotorControllerEnabled)
                                        targetRefreshCalls.Add($"if (m_activeRobotId == RobotIdentifier::{s}) {Environment.NewLine}{mc.GenerateCyclicGenericTargetRefresh()}");
                                }
                                else if (robotsWithMotorControllerEnabled.Count() > 0)
                                    targetRefreshCalls.Add(mc.GenerateCyclicGenericTargetRefresh());
                            }
                        }
                        resultString = resultString.Replace("$$_CYCLIC_GENERIC_TARGET_REFRESH_$$", ListToString(targetRefreshCalls, ";"));

                        #region Generate fixed decorator Cpp File

                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);

                        List<string> stateTransitions = new List<string>();
                        List<string> statesCreation = new List<string>();
                        int stateIndex = 0;
                        foreach (state s in mi.mechanism.states)
                        {
                            statesCreation.AddRange(s.generateIndexedObjectCreation(stateIndex));
                            stateIndex++;

                            if (s.transitionsTo.Count > 0)
                            {
                                foreach (stringParameterConstInMechInstance transition in s.transitionsTo)
                                {
                                    stateTransitions.Add(String.Format("{0}StateInst->RegisterTransitionState({1}StateInst)", s.name, transition.value));
                                }
                            }
                            else
                            {
                                stateTransitions.Add(String.Format("{0}StateInst->RegisterTransitionState({0}StateInst)", s.name));
                            }
                        }
                        resultString = resultString.Replace("$$_OBJECT_CREATION_$$", ListToString(statesCreation, ";"));
                        resultString = resultString.Replace("$$_STATE_TRANSITION_REGISTRATION_$$", ListToString(stateTransitions, ";"));

                        List<string> includeList = generateMethod(mi, "generateIncludes").Distinct().ToList();
                        resultString = resultString.Replace("$$_STATE_CLASSES_INCLUDES_$$", ListToString(includeList, ""));
                        #endregion


                        filePathName = getMechanismFullFilePathName(mechanismName, cdf.outputFilePathName.Replace("MECHANISM_INSTANCE_NAME", mechanismName), true);
                        copyrightAndGenNoticeAndSave(filePathName, resultString);
                        #endregion

                        #region Generate H File
                        generatorContext.generationStage = generatorContext.GenerationStage.MechInstanceGenH;

                        cdf = theToolConfiguration.getTemplateInfo("MechanismInstance_h");
                        template = loadTemplate(cdf.templateFilePathName);

                        resultString = template;

                        #region clean up unused stuff
                        if (index < robot.mechanismInstances.Count)
                        {
                            resultString = resultString.Replace("_STATE_MANAGER_START_", "");
                            resultString = resultString.Replace("_STATE_MANAGER_END_", "");
                        }
                        else
                            resultString = Remove(resultString, "_STATE_MANAGER_START_", "_STATE_MANAGER_END_");
                        #endregion

                        resultString = CleanMotorMechanismCode(mi, resultString);
                        resultString = CleanSolenoidMechanismCode(mi, resultString);
                        resultString = CleanServoMechanismCode(mi, resultString);
                        resultString = CleanNtTuningMechanismCode(mi, resultString);

                        resultString = resultString.Replace("$$_INCLUDE_FILES_$$", ListToString(generateMethod(mi.mechanism, "generateIncludes").Distinct().ToList()));

                        resultString = resultString.Replace("$$_CREATE_FUNCTIONS_$$", createFunctionDeclarations);
                        resultString = resultString.Replace("$$_PUBLIC_INITIALZATION_FUNCTIONS_$$", publicInitializationFunctionDeclarations);
                        resultString = resultString.Replace("$$_PRIVATE_INITIALZATION_FUNCTIONS_$$", privateInitializationFunctionDeclarations);
                            
                        resultString = resultString.Replace("$$_MECHANISM_NAME_$$", mi.mechanism.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_UPPER_CASE_$$", ToUnderscoreCase(mi.name).ToUpper());

                        //=============== generate the mechanism member variables
                        List<string> mechElements = generateMethod(mi.mechanism, "generateDefinition").FindAll(me => !me.StartsWith("state* "));
                        resultString = resultString.Replace("$$_MECHANISM_ELEMENTS_$$", ListToString(mechElements));

                        //=============== generate the get functions for the mechanism member variables
                        List<string> mechElementsGetters = generateMethod(mi.mechanism, "generateDefinitionGetter").FindAll(me => !me.StartsWith("state* "));
                        resultString = resultString.Replace("$$_MECHANISM_ELEMENTS_GETTERS_$$", ListToString(mechElementsGetters.Distinct().ToList()));

                        List<string> targetVariables = new List<string>();
                        List<string> targetUpdateFunctions = new List<string>();
                        List<string> genericTargetVariables = new List<string>();
                        foreach (state s in generatorContext.theMechanismInstance.mechanism.states)
                        {
                            foreach (motorTarget mt in s.motorTargets)
                            {
                                motorControlData mcd = mi.mechanism.stateMotorControlData.Find(c => c.name == mt.controlDataName);
                                List<MotorController> mcs = mi.mechanism.MotorControllers.FindAll(m => m.name == mt.motorName);
                                foreach (MotorController mc in mcs)
                                {
                                    if (mcd != null && mc != null)
                                    {
                                        targetUpdateFunctions.AddRange(mc.GenerateTargetUpdateFunctions(mcd));
                                        targetVariables.Add(mc.GenerateTargetMemberVariable(mcd));
                                        genericTargetVariables.Add(mc.GenerateGenericTargetMemberVariable());
                                    }
                                }
                            }
                        }

                        if (targetVariables.Count > 0)
                            targetVariables.AddRange(genericTargetVariables.Distinct());
                        
                        List<string> loggingVariables = generateMethod(mi.mechanism, "generateLoggingObjects");
                        loggingVariables.Add(string.Format("wpi::log::DoubleEntry m_{0}TotalEnergyLogEntry;", mi.name));
                        loggingVariables.Add(string.Format("wpi::log::DoubleEntry m_{0}TotalWattHoursLogEntry;", mi.name));
                        loggingVariables.Add(string.Format("wpi::log::IntegerLogEntry m_{0}StateLogEntry;", mi.name));
                        loggingVariables.Add("frc::Timer m_powerTimer;");
                        loggingVariables.Add("double m_power = 0.0;");
                        loggingVariables.Add("double m_energy = 0.0;");
                        loggingVariables.Add("double m_totalEnergy = 0.0;");
                        loggingVariables.Add("double m_totalWattHours = 0.0;");


                        List<string> loggingMethods = generateMethod(mi.mechanism, "generateLoggingMethods");
                        loggingMethods.Add(string.Format("void Log{0}TotalEnergy(uint64_t timestamp, int value) {{return m_{0}TotalEnergyLogEntry.Update(value, timestamp);}}", mi.name));
                        loggingMethods.Add(string.Format("void Log{0}TotalWattHours(uint64_t timestamp, int value) {{return m_{0}TotalWattHoursLogEntry.Update(value, timestamp);}}", mi.name));
                        loggingMethods.Add(string.Format("void Log{0}State(uint64_t timestamp, int value) {{return m_{0}StateLogEntry.Update(value, timestamp);}}", mi.name));
                        

                        resultString = resultString.Replace("$$_TARGET_UPDATE_FUNCTIONS_$$", ListToString(targetUpdateFunctions.Distinct().ToList()));
                        resultString = resultString.Replace("$$_TARGET_MEMBER_VARIABLES_$$", ListToString(targetVariables.Distinct().ToList()));
                        resultString = resultString.Replace("$$_LOGGING_OBJECTS_$$", ListToString(loggingVariables.Distinct().ToList()));
                        resultString = resultString.Replace("$$_LOGGING_FUNCTIONS_$$", ListToString(loggingMethods.Distinct().ToList()));

                        //closed loop parameters
                        string allParameters = "";
                        resultString = resultString.Replace("$$_TUNABLE_PARAMETERS_$$", allParameters);

                        List<string> enumList = new List<string>();
                        foreach (state s in mi.mechanism.states)
                        {
                            enumList.Add(String.Format("STATE_{0}", ToUnderscoreCase(s.name).ToUpper()));
                        }

                        resultString = resultString.Replace("$$_STATE_NAMES_$$", ListToString(enumList, ", ").TrimEnd(new char[] { ',', ' ' }));

                        filePathName = getMechanismFullFilePathName(mechanismName, cdf.outputFilePathName.Replace("MECHANISM_INSTANCE_NAME", mechanismName), true);
                        copyrightAndGenNoticeAndSave(filePathName, resultString);
                        #endregion

                        if (index < robot.mechanismInstances.Count)
                        {
                            StringBuilder setTargetFunctionDeclerations = new StringBuilder();

                            generatorContext.generationStage = generatorContext.GenerationStage.MechInstanceDecorator;

                            #region The decorator mod files
                            createMechanismFolder(mechanismName, false);

                            #region Generate H StateGen_Decorator Files
                            foreach (state s in mi.mechanism.states)
                            {
                                cdf = theToolConfiguration.getTemplateInfo("state_h");
                                template = loadTemplate(cdf.templateFilePathName);

                                resultString = template;

                                resultString = resultString.Replace("$$_MECHANISM_NAME_$$", mi.mechanism.name);
                                resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);
                                resultString = resultString.Replace("$$_STATE_NAME_$$", s.name);

                                StringBuilder targetConstants = new StringBuilder();
                                foreach (motorTarget mT in s.motorTargets)
                                {
                                    motorControlData mcd = mi.mechanism.stateMotorControlData.Find(cd => cd.name == mT.controlDataName);
                                    MotorController mc = mi.mechanism.MotorControllers.Find(m => m.name == mT.motorName);
                                    if (mc != null && mcd != null && !mc.enableFollowID.value)
                                    {
                                        string targetType = ControlDataMapping.TryGetValue(mcd.controlType, out var value) ? value : "double";
                                        targetConstants.AppendLine($"const {targetType} m_{mT.motorName}Target = {targetType}({mT.target.value});");
                                    }
                                }
                                foreach (solenoidTarget sT in s.solenoidTarget)
                                {
                                    targetConstants.AppendLine($"const bool m_{sT.solenoidName}Target = {sT.target.value.ToString().ToLower()};");
                                }
                                resultString = resultString.Replace("$$_TARGET_VALUE_CONSTANT_$$", targetConstants.ToString().Trim());

                                List<string> stateElements = generateMethod(s, "generateDefinition");
                                resultString = resultString.Replace("$$_USER_VALUE_CONSTANT_$$", ListToString(stateElements));

                                resultString = resultString.Replace("$$_INCLUDE_FILES_$$", ListToString(generateMethod(s, "generateIncludes").Distinct().ToList()));

                                setTargetFunctionDeclerations = new StringBuilder();
                                foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
                                {
                                    mechanismInstance theMi = r.mechanismInstances.Find(m => m.mechanism.GUID == mi.mechanism.GUID);
                                    if (theMi != null)//if this mechanism exists in this robot
                                    {
                                        setTargetFunctionDeclerations.AppendLine(String.Format("void Init{0}();", r.getFullRobotName()));
                                    }
                                }

                                resultString = resultString.Replace("$$_STATE_INIT_FUNCTION_DECLS_$$", setTargetFunctionDeclerations.ToString().Trim());

                                filePathName = getMechanismFullFilePathName(mechanismName,
                                                                            cdf.outputFilePathName.Replace("MECHANISM_INSTANCE_NAME", mechanismName).Replace("STATE_NAME", s.name)
                                                                            , false);
                                copyrightAndGenNoticeAndSave(filePathName, resultString, true);
                            }
                            #endregion

                            #region Generate CPP StateGen_Decorator Files
                            foreach (state s in mi.mechanism.states)
                            {
                                cdf = theToolConfiguration.getTemplateInfo("state_cpp");
                                template = loadTemplate(cdf.templateFilePathName);

                                resultString = template;

                                resultString = resultString.Replace("$$_MECHANISM_NAME_$$", mi.mechanism.name);
                                resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);
                                resultString = resultString.Replace("$$_STATE_NAME_$$", s.name);

                                StringBuilder setTargetFunctionCalls = new StringBuilder();
                                StringBuilder setTargetFunctionDefinitions = new StringBuilder();
                                foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
                                {
                                    mechanismInstance theMi = r.mechanismInstances.Find(m => m.mechanism.GUID == mi.mechanism.GUID);
                                    //if this mechanism exists in this robot
                                    if (theMi != null)
                                    {
                                        state instanceState = theMi.mechanism.states.Find(st => st.name == s.name);
                                        StringBuilder stateTargets = new StringBuilder();
                                        List<string> motorTargets = new List<string>();
                                        foreach (motorTarget mT in instanceState.motorTargets)
                                        {
                                            if (mT.Enabled.value)
                                            {
                                                // find the corresponding motor control Data link
                                                motorControlData mcd = theMi.mechanism.stateMotorControlData.Find(cd => cd.name == mT.controlDataName);
                                                if (mcd == null)
                                                    addProgress(string.Format("In mechanism {0}, cannot find a Motor control data called {1}, referenced in state {2}", theMi.name, mT.controlDataName, s.name));
                                                else
                                                {
                                                    //void SetTargetControl(RobotElementNames::MOTOR_CONTROLLER_USAGE identifier, double percentOutput);
                                                    //void SetTargetControl(RobotElementNames::MOTOR_CONTROLLER_USAGE identifier, ControlData &controlConst, units::angle::degree_t angle );
                                                    //void SetTargetControl(RobotElementNames::MOTOR_CONTROLLER_USAGE identifier, ControlData &controlConst, units::angular_velocity::revolutions_per_minute_t angVel );
                                                    //void SetTargetControl(RobotElementNames::MOTOR_CONTROLLER_USAGE identifier, ControlData &controlConst, units::length::inch_t position );
                                                    //void SetTargetControl(RobotElementNames::MOTOR_CONTROLLER_USAGE identifier, ControlData &controlConst, units::velocity::feet_per_second_t velocity );

                                                    string targetUnitsType = "";
                                                    if (mcd.controlType == motorControlData.CONTROL_TYPE.PERCENT_OUTPUT) { }
                                                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_INCH) { targetUnitsType = "units::length::inch_t"; }
                                                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.POSITION_DEGREES) { targetUnitsType = "units::angle::degree_t"; }
                                                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.VELOCITY_FEET_PER_SEC) { targetUnitsType = "units::velocity::feet_per_second_t"; }
                                                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.VELOCITY_DEGREES_PER_SEC) { targetUnitsType = "units::angular_velocity::degrees_per_second_t"; }
                                                    else if (mcd.controlType == motorControlData.CONTROL_TYPE.VELOCITY_REV_PER_SEC) { targetUnitsType = "units::angular_velocity::turns_per_second_t"; }
                                                    //else if (mcd.controlType == motorControlData.CONTROL_TYPE.VOLTAGE) { targetUnitsType = "units::angular_velocity::revolutions_per_minute_t"; }
                                                    //else if (mcd.controlType == motorControlData.CONTROL_TYPE.CURRENT) { addProgress("How should we handle CURRENT"); }
                                                    //else if (mcd.controlType == motorControlData.CONTROL_TYPE.TRAPEZOID_LINEAR_POS) { targetUnitsType = "units::length::inch_t"; }
                                                    //else if (mcd.controlType == motorControlData.CONTROL_TYPE.TRAPEZOID_ANGULAR_POS) { targetUnitsType = "units::angle::degree_t"; }

                                                    MotorController mc = theMi.mechanism.MotorControllers.Find(m => m.name == mT.motorName);
                                                    if (mc == null)
                                                    {
                                                        addProgress(string.Format("In mechanism {0}, cannot find a Motor controller called {1}, referenced in state {2}, target {3}", theMi.name, mT.motorName, s.name, mT.name));
                                                    }
                                                    else
                                                    {
                                                        string motorEnumName = String.Format("RobotElementNames::{0}", ListToString(mc.generateElementNames(), "").Trim().Replace("::", "_USAGE::").ToUpper());
                                                        if (targetUnitsType == "")
                                                        {
                                                            if (mc.GetType().IsSubclassOf(typeof(SparkController)))
                                                            {
                                                                motorTargets.Add(String.Format("Get{0}()->get{1}()->SetControlConstants({2},*Get{0}()->get{3}())", theMi.name, mc.name, 0 /*slot number fixed to 0*/, mT.controlDataName));
                                                            }

                                                            motorTargets.Add(String.Format("SetTargetControl({0}, {1})", motorEnumName, mT.target.value));
                                                        }
                                                        else
                                                            motorTargets.Add(String.Format("SetTargetControl({0}, Get{1}()->get{2}(), {5}({3}({4})))",
                                                                motorEnumName,
                                                                theMi.name,
                                                                mcd.name,
                                                                generatorContext.theGeneratorConfig.getWPIphysicalUnitType(mT.target.physicalUnits),
                                                                mT.target.value,
                                                                targetUnitsType));
                                                    }
                                                }
                                            }
                                        }

                                        stateTargets.AppendLine(ListToString(motorTargets, ";"));

                                        setTargetFunctionCalls.AppendLine(string.Format("else if(m_RobotId == RobotIdentifier::{0})", ToUnderscoreDigit(ToUnderscoreCase(r.getFullRobotName())).ToUpper()));
                                        setTargetFunctionCalls.AppendLine(String.Format(" Init{0}();", r.getFullRobotName()));

                                        setTargetFunctionDeclerations.AppendLine(String.Format("void Init{0}();", r.getFullRobotName()));

                                        setTargetFunctionDefinitions.AppendLine(String.Format("void {0}State::Init{1}()", s.name, r.getFullRobotName()));
                                        setTargetFunctionDefinitions.AppendLine("{");
                                        foreach (motorTarget mT in instanceState.motorTargets)
                                        {
                                            if (mT.Enabled.value == true)
                                            {
                                                motorControlData mcd = theMi.mechanism.stateMotorControlData.Find(cd => cd.name == mT.controlDataName);
                                                MotorController mc = theMi.mechanism.MotorControllers.Find(m => (m.name == mT.motorName) && (m.ControllerEnabled == MotorController.Enabled.Yes));
                                                if (mc == null)
                                                    addProgress(string.Format("In mechanism {0}, cannot find a Motor controller called {1}, referenced in state {2}, target {3}", theMi.name, mT.motorName, s.name, mT.name));
                                                else if (mcd == null)
                                                    addProgress(string.Format("In mechanism {0}, cannot find a Motor control data called {1}, referenced in state {2}", theMi.name, mT.controlDataName, s.name));
                                                else
                                                {
                                                    //string PidSetCall = mc.GeneratePIDSetFunctionCall(mcd, theMi);
                                                    //if (!string.IsNullOrEmpty(PidSetCall))
                                                    //    setTargetFunctionDefinitions.AppendLine(string.Format("m_mechanism->{0};", PidSetCall));
                                                    if (!mc.enableFollowID.value)
                                                        setTargetFunctionDefinitions.AppendLine(string.Format("m_mechanism->{0};", mc.GenerateTargetUpdateFunctionCall(mcd, mT.target.value)));
                                                }
                                            }
                                        }
                                        foreach (solenoidTarget sT in s.solenoidTarget)
                                        {
                                            if (sT.Enabled.value)
                                                setTargetFunctionDefinitions.AppendLine($"m_mechanism->Get{sT.solenoidName}->Set (s_{sT.solenoidName}Target);");
                                        }
                                        setTargetFunctionDefinitions.AppendLine("}");
                                        setTargetFunctionDefinitions.AppendLine();

                                    }
                                }
                                resultString = resultString.Replace("$$_STATE_INIT_FUNCTION_CALLS_$$", setTargetFunctionCalls.ToString().Trim().Substring(5));
                                resultString = resultString.Replace("$$_STATE_INIT_FUNCTIONS_$$", setTargetFunctionDefinitions.ToString().Trim());

                                filePathName = getMechanismFullFilePathName(mechanismName,
                                                                            cdf.outputFilePathName.Replace("MECHANISM_INSTANCE_NAME", mechanismName).Replace("STATE_NAME", s.name)
                                                                            , false);
                                copyrightAndGenNoticeAndSave(filePathName, resultString, true);
                            }
                            #endregion

                            #endregion
                        }

                        if (cleanMode)
                        {
                            DeleteDirectory(getMechanismOutputPath(mechanismName, true));
                            if (cleanDecoratorModFolders)
                            {
                                DeleteDirectory(getMechanismOutputPath(mechanismName, false));
                            }
                        }
                    }

                    index++;
                }
            }
        }

        void DeleteDirectory(string path)
        {
            if (path.Contains(@"cpp\mechanisms")) // this is just for safety... we do not want to erase the whole drive
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        private string CleanSolenoidMechanismCode(mechanismInstance mi, string resultString)
        {
            string startStr = "_MECHANISM_HAS_SOLENOIDS_START_";
            string endStr = "_MECHANISM_HAS_SOLENOIDS_END_";

            if (mi.mechanism.solenoid.Count == 0)
                resultString = Remove(resultString, startStr, endStr);
            else
            {
                resultString = resultString.Replace(startStr, "");
                resultString = resultString.Replace(endStr, "");
            }

            return resultString;
        }
        private string CleanNtTuningMechanismCode(mechanismInstance mi, string resultString)
        {
            string startCallsStr = "_NT_TUNING_FUNCTION_CALLS_START_";
            string endCallsStr = "_NT_TUNING_FUNCTION_CALLS_END_";
            string startStr = "_NT_TUNING_FUNCTIONS_START_";
            string endStr = "_NT_TUNING_FUNCTIONS_END_";

            if (true)
            {
                resultString = Remove(resultString, startCallsStr, endCallsStr);
                resultString = Remove(resultString, startStr, endStr);
            }
            else
            {
                resultString = resultString.Replace(startCallsStr, "");
                resultString = resultString.Replace(endCallsStr, "");
                resultString = resultString.Replace(startStr, "");
                resultString = resultString.Replace(endStr, "");
            }

            return resultString;
        }

        private string CleanServoMechanismCode(mechanismInstance mi, string resultString)
        {
            string startStr = "_MECHANISM_HAS_SERVOS_START_";
            string endStr = "_MECHANISM_HAS_SERVOS_END_";

            if (mi.mechanism.servo.Count == 0)
                resultString = Remove(resultString, startStr, endStr);
            else
            {
                resultString = resultString.Replace(startStr, "");
                resultString = resultString.Replace(endStr, "");
            }

            return resultString;
        }


        private string CleanMotorMechanismCode(mechanismInstance mi, string resultString)
        {
            string startStr = "_MECHANISM_HAS_MOTORS_START_";
            string endStr = "_MECHANISM_HAS_MOTORS_END_";

            if (mi.mechanism.MotorControllers.Count == 0)
                resultString = Remove(resultString, startStr, endStr);
            else
            {
                resultString = resultString.Replace(startStr, "");
                resultString = resultString.Replace(endStr, "");
            }

            foreach (motorControlData.CONTROL_TYPE ct in Enum.GetValues(typeof(motorControlData.CONTROL_TYPE)).Cast<motorControlData.CONTROL_TYPE>())
            {
                startStr = "_UPDATE_TARGET_" + ct.ToString() + "_START_";
                endStr = "_UPDATE_TARGET_" + ct.ToString() + "_END_";

                if (mi.mechanism.stateMotorControlData.Find(mcd => mcd.controlType == ct) == null)
                    resultString = Remove(resultString, startStr, endStr);
                else
                {
                    resultString = resultString.Replace(startStr, "");
                    resultString = resultString.Replace(endStr, "");
                }
            }

            return resultString;
        }

        private string GenerateCreateFunctions(mechanismInstance mi, out string functionDeclarations)
        {
            string createFunctionTemplate =
                            @"void $$_MECHANISM_INSTANCE_NAME_$$::Create$$_ROBOT_FULL_NAME_$$()
                                {
                                    m_ntName = ""$$_MECHANISM_INSTANCE_NAME_$$"";
                                    $$_OBJECT_CREATION_$$
                                    
                                    ReadConstants(""$$_MECHANISM_INSTANCE_NAME_$$.xml"", $$_ROBOT_ID_$$);

                                    _NT_TUNING_FUNCTION_CALLS_START_
                                    m_table = nt::NetworkTableInstance::GetDefault().GetTable(m_ntName);
                                    m_tuningIsEnabledStr = ""Enable Tuning for "" + m_ntName; // since this string is used every loop, we do not want to create the string every time
                                    m_table.get()->PutBoolean(m_tuningIsEnabledStr, m_tuning);
                                    _NT_TUNING_FUNCTION_CALLS_END_
                                }";

            string createFunctionDeclarationTemplate = "void Create$$_ROBOT_FULL_NAME_$$()";

            List<string> createCode = new List<string>();
            List<string> createDeclarationCode = new List<string>();
            foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
            {
                generatorContext.theRobot = r;
                mechanismInstance mis = r.mechanismInstances.Find(m => m.name == mi.name);
                if (mis != null)
                {
                    generatorContext.theMechanismInstance = mis;
                    string temp = createFunctionTemplate;
                    temp = temp.Replace("$$_OBJECT_CREATION_$$", ListToString(generateMethod(mis, "generateIndexedObjectCreation")));
                    temp = temp.Replace("$$_ROBOT_FULL_NAME_$$", r.getFullRobotName());
                    temp = temp.Replace("$$_ROBOT_ID_$$", r.robotID.value.ToString());
                    createCode.Add(temp);

                    string tempDecl = createFunctionDeclarationTemplate;
                    createDeclarationCode.Add(tempDecl.Replace("$$_ROBOT_FULL_NAME_$$", r.getFullRobotName()));
                }
            }

            functionDeclarations = ListToString(createDeclarationCode, ";");

            return ListToString(createCode, Environment.NewLine);
        }

        private string GenerateInitializationFunctions(mechanismInstance mi, out string publicFunctionDeclarations, out string privateFunctionDeclarations)
        {
            string createFunctionTemplate =
                            @"void $$_MECHANISM_INSTANCE_NAME_$$::Initialize$$_ROBOT_FULL_NAME_$$()
                                {
                                    $$_ELEMENT_INITIALIZATION_$$
                                }";

            string createFunctionDeclarationTemplate = "void Initialize$$_ROBOT_FULL_NAME_$$()";

            List<string> createCode = new List<string>();
            List<string> createCodeFunctions = new List<string>();
            List<string> publicInitDeclarationCode = new List<string>();
            List<string> privateInitDeclarationCode = new List<string>();
            foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
            {
                mechanismInstance mis = r.mechanismInstances.Find(m => m.name == mi.name);
                if (mis != null)
                {
                    string temp = createFunctionTemplate;

                    generatorContext.theMechanismInstance = mis;

                    List<string> initFunctions = generateMethod(mis, "generateInitialization");
                    List<string> initFunctionCalls = initFunctions.FindAll(s => s.StartsWith("CALL:"));
                    List<string> initFunctionDeclarations = initFunctions.FindAll(s => s.StartsWith("DECLARATION:"));

                    temp = temp.Replace("$$_ELEMENT_INITIALIZATION_$$", ListToString(initFunctionCalls, ";").Replace("CALL:", ""));
                    temp = temp.Replace("$$_ROBOT_FULL_NAME_$$", r.getFullRobotName());
                    createCode.Add(temp);

                    string function = ListToString(initFunctions.FindAll(s => !s.StartsWith("CALL:") && !s.StartsWith("DECLARATION:"))).Replace("$$_ROBOT_FULL_NAME_$$", r.getFullRobotName());
                    createCodeFunctions.Add(function);

                    string tempDecl = createFunctionDeclarationTemplate;
                    publicInitDeclarationCode.Add(tempDecl.Replace("$$_ROBOT_FULL_NAME_$$", r.getFullRobotName()));
                    foreach (string s in initFunctionDeclarations)
                        privateInitDeclarationCode.Add(s.Replace("$$_ROBOT_FULL_NAME_$$", r.getFullRobotName()).Replace("DECLARATION:", ""));
                }
            }

            publicFunctionDeclarations = ListToString(publicInitDeclarationCode, ";");
            privateFunctionDeclarations = ListToString(privateInitDeclarationCode, ";");

            return ListToString(createCode, Environment.NewLine) + Environment.NewLine + ListToString(createCodeFunctions);
        }


        internal string getIncludePath(string mechanismName, bool generated)
        {
            return getMechanismOutputPath(mechanismName, generated).Replace(theToolConfiguration.rootOutputFolder, "").Replace(@"\", "/").TrimStart('/');
        }

        internal void createMechanismFolder(string mechanismName, bool generated)
        {
            Directory.CreateDirectory(getMechanismOutputPath(mechanismName, generated));
        }

        internal string getMechanismFullFilePathName(string mechanismName, string templateFilePath, bool generated)
        {
            string filename = Path.GetFileName(templateFilePath);

            filename = filename.Replace("MECHANISM_NAME", mechanismName);

            return Path.Combine(getMechanismOutputPath(mechanismName, generated), filename);
        }

        internal string getMechanismOutputPath(string mechanismName, bool generated)
        {
            return Path.Combine(theToolConfiguration.GetGeneratedSourceCodeBasePath(), "mechanisms", mechanismName);
        }

        internal string getDeployOutputPath()
        {
            return Path.Combine(theToolConfiguration.GetGeneratedDeployBasePath(), @"..\deploy");
        }
    }
}
