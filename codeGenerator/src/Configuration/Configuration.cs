﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace Configuration
{
    [Serializable]
    public class toolConfiguration
    {
        [XmlIgnore]
        public string configurationFullPath = "";

        [XmlIgnore]
        public string rootOutputFolder = "";

        public string sourceCodeRelativePath = @"main\cpp";
        public string deployRelativePath = @"main\deploy";
        public string robotConfiguration = "";
        public List<string> appDataConfigurations = new List<string>();

        public List<string> collectionBaseTypes = new List<string>();

        public List<physicalUnit> physicalUnits = new List<physicalUnit>();

        public List<codeTemplateFile> codeTemplateFiles = new List<codeTemplateFile>();

        public string CopyrightNotice = "";
        public string GenerationNotice = "";
        public string EditorFormattingDisable = "";

        public void loadDummyData()
        {
        }
        public override string ToString()
        {
            return "";
        }

        public string GetGeneratedSourceCodeBasePath()
        {
            return Path.Combine(rootOutputFolder,sourceCodeRelativePath);
        }

        public string GetGeneratedDeployBasePath()
        {
            return Path.Combine(rootOutputFolder, deployRelativePath);
        }

        public string getWPIphysicalUnitType(string unitShortName)
        {
            physicalUnit pu = physicalUnits.Find(u => u.shortName == unitShortName);
            if (pu != null)
                return pu.wpiClassName;

            return "";
        }
        public string getWPIphysicalUnitIncludeFile(string unitShortName)
        {
            physicalUnit pu = physicalUnits.Find(u => u.shortName == unitShortName);
            if (pu != null)
                return $"{pu.family}.h";

            return "";
        }

        string rootOutputFolder_temp;
        string robotConfiguration_temp;
        private void preSerialize()
        {
            // make the paths relative to the configuration file
            string rootPath = Path.GetDirectoryName(configurationFullPath);

            rootOutputFolder_temp = rootOutputFolder;
            rootOutputFolder = RelativePath(rootPath, rootOutputFolder);

            robotConfiguration_temp = robotConfiguration;
            robotConfiguration = RelativePath(rootPath, robotConfiguration);

            this.physicalUnits.RemoveAll(p => p.family == physicalUnit.Family.all);
        }

        private void postSerialize()
        {
            rootOutputFolder = rootOutputFolder_temp;
            robotConfiguration = robotConfiguration_temp;

            int cnt = physicalUnits.Count;
            for (int i = 0; i < cnt; i++)
            {
                physicalUnit temp = new physicalUnit();
                temp.family = physicalUnit.Family.all;
                temp.shortName = physicalUnits[i].shortName;
                temp.longName = physicalUnits[i].longName;
                temp.wpiClassName = physicalUnits[i].wpiClassName;

                physicalUnits.Add(temp);
            }

        }
        private void postDeSerialize(toolConfiguration tc)
        {
            int cnt = tc.physicalUnits.Count;
            for (int i = 0; i < cnt; i++)
            {
                physicalUnit temp = new physicalUnit();
                temp.family = physicalUnit.Family.all;
                temp.shortName = tc.physicalUnits[i].shortName;
                temp.longName = tc.physicalUnits[i].longName;
                temp.wpiClassName = tc.physicalUnits[i].wpiClassName;

                tc.physicalUnits.Add(temp);
            }
        }
        public void serialize(string rootPath)
        {
            preSerialize();

            string fullFilePath = Path.Combine(rootPath, @"configuration.xml");

            var mySerializer = new XmlSerializer(typeof(toolConfiguration));

            string configAsString = "";
            using (StringWriter textWriter = new StringWriter())
            {
                mySerializer.Serialize(textWriter, this);
                configAsString = textWriter.ToString();
            }

            string currentFileContents = File.ReadAllText(fullFilePath);

            // when we serialize to string somehow it adds the encoding info
            // remove it before comparing
            configAsString = configAsString.Replace(" encoding=\"utf-16\"", "");

            // also \r\n are generated in a different way, so just make them identical before comparing
            configAsString = configAsString.Replace("\r\n", "\r");
            currentFileContents = currentFileContents.Replace("\r\n", "\r");
            configAsString = configAsString.Replace("\n", "\r");
            currentFileContents = currentFileContents.Replace("\n", "\r");

            if (!currentFileContents.Equals(configAsString))
            {
                using (var myFileStream = new FileStream(fullFilePath, FileMode.Create))
                {
                    mySerializer.Serialize(myFileStream, this);
                }
            }

            postSerialize();
        }
        public toolConfiguration deserialize(string fullFilePathName)
        {
            var mySerializer = new XmlSerializer(typeof(toolConfiguration));

            using (var myFileStream = new FileStream(fullFilePathName, FileMode.Open))
            {
                toolConfiguration tc = (toolConfiguration)mySerializer.Deserialize(myFileStream);
                tc.configurationFullPath = fullFilePathName;

                postDeSerialize(tc);

                return tc;
            }
        }

        public codeTemplateFile getTemplateInfo(string name)
        {
            return this.codeTemplateFiles.Find(t => t.name == name);
        }

        public string RelativePath(string absPath, string relTo)
        {
            string[] absDirs = absPath.Split('\\');
            string[] relDirs = relTo.Split('\\');

            // Get the shortest of the two paths
            int len = absDirs.Length < relDirs.Length ? absDirs.Length :
            relDirs.Length;

            // Use to determine where in the loop we exited
            int lastCommonRoot = -1;
            int index;

            // Find common root
            for (index = 0; index < len; index++)
            {
                if (absDirs[index] == relDirs[index]) lastCommonRoot = index;
                else break;
            }

            // If we didn't find a common prefix then throw
            if (lastCommonRoot == -1)
            {
                throw new ArgumentException("Paths do not have a common base");
            }

            // Build up the relative path
            StringBuilder relativePath = new StringBuilder();

            // Add on the ..
            for (index = lastCommonRoot + 1; index < absDirs.Length; index++)
            {
                if (absDirs[index].Length > 0) relativePath.Append("..\\");
            }

            // Add on the folders
            for (index = lastCommonRoot + 1; index < relDirs.Length - 1; index++)
            {
                relativePath.Append(relDirs[index] + "\\");
            }
            relativePath.Append(relDirs[relDirs.Length - 1]);

            return relativePath.ToString();
        }


    }

    [Serializable]
    public class codeTemplateFile
    {
        public string name { get; set; }
        public string templateFilePathName { get; set; }
        public string outputFilePathName { get; set; }
    }

    [Serializable]
    public class physicalUnit
    {
        public enum Family { none, all, angle, angularAcceleration, angularVelocity, length, mass, current, voltage, acceleration, percent, power, pressure, time, velocity }
        public string shortName { get; set; }
        public string longName { get; set; }
        public Family family { get; set; }
        public string wpiClassName { get; set; }

        public override string ToString()
        {
            return shortName;
        }
    }
}
