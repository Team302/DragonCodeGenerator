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
//using ApplicationData.motorControlData;

namespace CoreCodeGenerator
{
    internal class MechanismInstanceGenerator : baseGenerator
    {
        internal MechanismInstanceGenerator(string codeGeneratorVersion, applicationDataConfig theRobotConfiguration, toolConfiguration theToolConfiguration, bool cleanMode, bool cleanDecoratorModFolders, showMessage displayProgress)
        : base(codeGeneratorVersion, theRobotConfiguration, theToolConfiguration, cleanMode, cleanDecoratorModFolders)
        {
            setProgressCallback(displayProgress);
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
                mechInstances.AddRange(robot.Chassis.mechanismInstances);

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

                        resultString = CleanMotorMechanismCode(mi, resultString);
                        resultString = CleanSolenoidMechanismCode(mi, resultString);
                        resultString = CleanServoMechanismCode(mi, resultString);

                        string createFunctionDeclarations;
                        resultString = resultString.Replace("$$_CREATE_FUNCTIONS_$$", GenerateCreateFunctions(mi, out createFunctionDeclarations));
                        string initializationFunctionDeclarations;
                        resultString = resultString.Replace("$$_INITIALZATION_FUNCTIONS_$$", GenerateInitializationFunctions(mi, out initializationFunctionDeclarations));

                        resultString = resultString.Replace("$$_MECHANISM_TYPE_NAME_$$", ToUnderscoreCase(mi.name).ToUpper());
                        resultString = resultString.Replace("$$_MECHANISM_NAME_$$", mi.mechanism.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_UPPER_CASE_$$", ToUnderscoreCase(mi.name).ToUpper());


                        List<string> theUsings = generateMethod(mi, "generateUsings").Distinct().ToList();
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
                                        allParameterReading += string.Format("{0}->Set{4}( m_table.get()->GetNumber(\"{0}_{1}\", {2}));{3}", mcd.name, pi.Name, pi.GetValue(obj), Environment.NewLine, setItem);
                                        allParameterWriting += string.Format(" m_table.get()->PutNumber(\"{0}_{1}\", {0}->Get{4}());{3}", mcd.name, pi.Name, pi.GetValue(obj), Environment.NewLine, setItem);
                                    }
                                }

                            }
                        }

                        resultString = resultString.Replace("$$_READ_TUNABLE_PARAMETERS_$$", allParameterReading);
                        resultString = resultString.Replace("$$_PUSH_TUNABLE_PARAMETERS_$$", allParameterWriting);

                        #endregion


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

                        resultString = resultString.Replace("$$_INCLUDE_FILES_$$", ListToString(generateMethod(mi.mechanism, "generateIncludes").Distinct().ToList()));

                        resultString = resultString.Replace("$$_CREATE_FUNCTIONS_$$", createFunctionDeclarations);
                        resultString = resultString.Replace("$$_INITIALZATION_FUNCTIONS_$$", initializationFunctionDeclarations);

                        resultString = resultString.Replace("$$_MECHANISM_NAME_$$", mi.mechanism.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_$$", mi.name);
                        resultString = resultString.Replace("$$_MECHANISM_INSTANCE_NAME_UPPER_CASE_$$", ToUnderscoreCase(mi.name).ToUpper());

                        List<string> mechElementsGetters = generateMethod(mi.mechanism, "generateDefinitionGetter").FindAll(me => !me.StartsWith("state* "));
                        resultString = resultString.Replace("$$_MECHANISM_ELEMENTS_GETTERS_$$", ListToString(mechElementsGetters.Distinct().ToList()));

                        List<string> mechElements = generateMethod(mi.mechanism, "generateDefinition").FindAll(me => !me.StartsWith("state* "));
                        resultString = resultString.Replace("$$_MECHANISM_ELEMENTS_$$", ListToString(mechElements));

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
                                        StringBuilder stateTargets = new StringBuilder();
                                        List<string> motorTargets = new List<string>();
                                        foreach (motorTarget mT in s.motorTargets)
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

                                        setTargetFunctionCalls.AppendLine(string.Format("else if(m_RobotId == RobotConfigMgr::RobotIdentifier::{0})", ToUnderscoreDigit(ToUnderscoreCase(robot.getFullRobotName())).ToUpper()));
                                        setTargetFunctionCalls.AppendLine(String.Format(" Init{0}();", r.getFullRobotName()));

                                        setTargetFunctionDeclerations.AppendLine(String.Format("void Init{0}();", r.getFullRobotName()));

                                        setTargetFunctionDefinitions.AppendLine(String.Format("void {0}State::Init{1}()", s.name, r.getFullRobotName()));
                                        setTargetFunctionDefinitions.AppendLine("{");
                                        setTargetFunctionDefinitions.AppendLine("// here set the targets ");
                                        setTargetFunctionDefinitions.AppendLine("/*");
                                        setTargetFunctionDefinitions.AppendLine(stateTargets.ToString().Trim());
                                        setTargetFunctionDefinitions.AppendLine("*/");
                                        setTargetFunctionDefinitions.AppendLine("}");
                                        setTargetFunctionDefinitions.AppendLine();

                                        resultString = resultString.Replace("$$_STATE_INIT_FUNCTION_CALLS_$$", setTargetFunctionCalls.ToString().Trim().Substring(5));
                                        resultString = resultString.Replace("$$_STATE_INIT_FUNCTIONS_$$", setTargetFunctionDefinitions.ToString().Trim());
                                    }
                                }

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
                            @"void $$_MECHANISM_INSTANCE_NAME_$$::Create$$_ROBOT_ID_$$()
                                {
                                    m_ntName = ""$$_MECHANISM_INSTANCE_NAME_$$"";
                                    $$_OBJECT_CREATION_$$

                                    m_table = nt::NetworkTableInstance::GetDefault().GetTable(m_ntName);
                                    m_tuningIsEnabledStr = ""Enable Tuning for "" + m_ntName; // since this string is used every loop, we do not want to create the string every time
                                    m_table.get()->PutBoolean(m_tuningIsEnabledStr, m_tuning);
                                }";

            string createFunctionDeclarationTemplate = "void Create$$_ROBOT_ID_$$()";

            List<string> createCode = new List<string>();
            List<string> createDeclarationCode = new List<string>();
            foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
            {
                mechanismInstance mis = r.mechanismInstances.Find(m => m.name == mi.name);
                if (mis != null)
                {
                    string temp = createFunctionTemplate;
                    temp = temp.Replace("$$_OBJECT_CREATION_$$", ListToString(generateMethod(mis, "generateIndexedObjectCreation")));
                    temp = temp.Replace("$$_ROBOT_ID_$$", r.getFullRobotName());
                    createCode.Add(temp);

                    string tempDecl = createFunctionDeclarationTemplate;
                    createDeclarationCode.Add(tempDecl.Replace("$$_ROBOT_ID_$$", r.getFullRobotName()));
                }
            }

            functionDeclarations = ListToString(createDeclarationCode, ";");

            return ListToString(createCode, Environment.NewLine);
        }

        private string GenerateInitializationFunctions(mechanismInstance mi, out string functionDeclarations)
        {
            string createFunctionTemplate =
                            @"void $$_MECHANISM_INSTANCE_NAME_$$::Initialize$$_ROBOT_ID_$$()
                                {
                                    $$_ELEMENT_INITIALIZATION_$$
                                }";

            string createFunctionDeclarationTemplate = "void Initialize$$_ROBOT_ID_$$()";

            List<string> createCode = new List<string>();
            List<string> initDeclarationCode = new List<string>();
            foreach (applicationData r in theRobotConfiguration.theRobotVariants.Robots)
            {
                mechanismInstance mis = r.mechanismInstances.Find(m => m.name == mi.name);
                if (mis != null)
                {
                    string temp = createFunctionTemplate;
                    temp = temp.Replace("$$_ELEMENT_INITIALIZATION_$$", ListToString(generateMethod(mis, "generateInitialization")));
                    temp = temp.Replace("$$_ROBOT_ID_$$", r.getFullRobotName());
                    createCode.Add(temp);

                    string tempDecl = createFunctionDeclarationTemplate;
                    initDeclarationCode.Add(tempDecl.Replace("$$_ROBOT_ID_$$", r.getFullRobotName()));
                }
            }

            functionDeclarations = ListToString(initDeclarationCode, ";");

            return ListToString(createCode, Environment.NewLine);
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
            return Path.Combine(theToolConfiguration.rootOutputFolder, "mechanisms", mechanismName);
        }

    }
}
