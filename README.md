# XML Preprocessor

[![GitHub license](https://img.shields.io/github/license/peitaosu/XML-Preprocessor.svg)](https://github.com/peitaosu/XML-Preprocessor/blob/master/LICENSE)

## What is XML Preprocessor ?

This is a XML Preprocessor which can be used to process your XML file before you use it, to process conditional statements, variables, iteration statements, error/warning, execute command, etc.

## XML Schema

### Include Files
```
<?include path/to/file ?>
```

### Variables
```
$(env.EnvironmentVariable)

$(sys.SystemVariable)

$(var.CustomVariable)
```

### Conditional Statements
```
<?if ?>

<?ifdef ?>

<?ifndef ?>

<?else?>

<?elseif ?>

<?endif?>
```

### Iteration Statements
```
<?foreach VARNAME in 1;2;3?>
    $(var.VARNAME)
<?endforeach?>
```

### Errors and Warnings
```
<?error "This is error message!" ?>

<?warning "This is warning message!" ?>
```

### Commands
```
<? cmd "echo hello world" ?>
```

## Usage

### python
```
from preprocessor import *

proc = Preprocessor()
proc.load("input.xml")
proc.preprocess()
proc.save("output.xml")

# command line
> python preprocessor.py <input.xml> [output.xml]
```

### C#
```
Preprocessor preprocessor = new Preprocessor();
XmlDocument processedXmlDoc = new XmlDocument();
processedXmlDoc = preprocessor.Process(inXml, null);
processedXmlDoc.Save(outXml);

# command line
> XMLPreprocessor.exe in.xml [out.xml]
```

