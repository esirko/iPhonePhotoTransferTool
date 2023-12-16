# iPhonePhotoTransferTool

Sometime between 2021-10 and 2022-04 Apple changed the way the folders are structured. It used to be in sequential folders that looked like `110APPLE`, `111APPLE`, etc. Now it appears to be tied to the month with a format like `202203__`, `202204__`. Some months have two folders like `202109_c` and `202109_d`.

This will copy all the files from the `201912__` folder to `E:\photos_import5\E11\201912__`:

```
sync 201912__ E:\photos_import5\E11
```

Here's a way to check that the copy succeeded as intended. Make sure this doesn't show anything. If any files were missed it would output them to stdout.

```
sync 201912__ E:\photos_import5\E11 --dry-run --quiet
```
