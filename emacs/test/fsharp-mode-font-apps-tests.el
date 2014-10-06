;;; fsharp-mode-font-apps-tests.el --- Regression test for FSharp font lock

;; Keywords: faces languages
;; Created: 2014-09-13
;; Version: 0.0.0

;; This program is free software: you can redistribute it and/or modify
;; it under the terms of the GNU General Public License as published by
;; the Free Software Foundation, either version 3 of the License, or
;; (at your option) any later version.

;; This program is distributed in the hope that it will be useful,
;; but WITHOUT ANY WARRANTY; without even the implied warranty of
;; MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
;; GNU General Public License for more details.

;; You should have received a copy of the GNU General Public License
;; along with this program.  If not, see <http://www.gnu.org/licenses/>.

;;; Commentary:

;; Regression test of `fsharp-mode-face', a package providing
;; font-lock rules for F#. This module verifies fontification of a
;; number of files taken from real projects. This is done by keeing a
;; text representation of the fontification using `faceup' markup, in
;; addition to the original source file.
;;
;; To regenerate the faceup representation, use M-x faceup-write-file RET.
;;
;; `faceup' is located at https://github.com/Lindydancer/faceup
;;
;; The actual check is performed using `ert' (Emacs Regression Test),
;; with a font-lock test function provided by `faceup'.

;;; Code:

(defvar fsharp-mode-face-test-apps-file-name load-file-name
  "The file name of this file.")

(defun fsharp-mode-face-test-apps-file-name ()
  "The filename of this source file."
  (or fsharp-mode-face-test-apps-file-name
      (symbol-file 'fsharp-mode-face-test-apps-file-name)))

(defun fsharp-mode-face-test-apps (file)
  "Test that FILE is fontifies as the .faceup file describes.

FILE is interpreted as relative to this source directory."
  (faceup-test-font-lock-file 'fsharp-mode
                              (concat
                               (file-name-directory
                                (fsharp-mode-face-test-apps-file-name))
                               file)))

(faceup-defexplainer fsharp-mode-face-test-apps)


(ert-deftest fsharp-mode-face-file-test ()
  (require 'faceup)
  (should (fsharp-mode-face-test-apps "apps/FQuake3/NativeMappings.fs"))
  (should (fsharp-mode-face-test-apps "apps/FSharp.Compatibility/Format.fs")))

;;; fsharp-mode-font-apps-tests.el ends here
