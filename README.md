# XML Preprocessor

## What is XML Preprocessor ?

This is a XML Preprocessor which can be used to process your XML file before you use it, to process conditional statements, variables, iteration statements, etc.

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


